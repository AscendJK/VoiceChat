using System.Buffers;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceChat.Core.Audio;

/// <summary>
/// 音频采集器 - 设备格式检测 + 声道归一 + 位深转换
/// </summary>
public class AudioCapture : IAudioCapture, IDisposable
{
    private WasapiCapture? _capture;
    private readonly object _lock = new();
    private readonly object _bufferLock = new();
    private readonly AudioPreprocessor _preprocessor = new();

    // 累积缓冲区
    private float[] _accumBuffer = new float[9600]; // 200ms @ 48kHz
    private int _accumPos = 0;

    // 预分配缓冲（避免GC）
    private float[] _monoBuffer = new float[4800];
    private short[] _pcmBuffer = new short[960];

    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 1;
    public int FrameSizeMs { get; set; } = 20;
    public int FrameSize => SampleRate * FrameSizeMs / 1000;
    public float InputVolume => System.Threading.Volatile.Read(ref _inputVolume);
    public AudioPreprocessor GetPreprocessor() => _preprocessor;
    public bool IsCapturing { get; private set; }

    /// <summary>
    /// 噪声门限开关（委托到前处理器）
    /// </summary>
    public bool NoiseGateEnabled
    {
        get => _preprocessor.NoiseGateEnabled;
        set => _preprocessor.NoiseGateEnabled = value;
    }

    /// <summary>
    /// 噪声门限阈值（委托到前处理器）
    /// </summary>
    public float NoiseGateThreshold
    {
        get => _preprocessor.NoiseGateThreshold;
        set => _preprocessor.NoiseGateThreshold = value;
    }

    // 输入音量事件
    public event Action<float>? InputVolumeChanged;

    // 当前输入音量（使用 Volatile 保证跨线程可见性）
    private float _inputVolume = 0f;

    /// <summary>
    /// 音频帧事件（每帧20ms触发一次，输出16位整型单声道）
    /// </summary>
    public event Action<short[], int>? OnFrameReady;

    public void Initialize(MMDevice? device = null)
    {
        lock (_lock)
        {
            _capture = device == null
                ? new WasapiCapture()
                : new WasapiCapture(device);

            // 强制使用 IEEE float 格式，确保 Capture_DataAvailable 能正确解析采样数据
            // 如果设备不支持则回退到 32-bit integer，最后回退到 16-bit（回调中会检测并正确转换）
            try
            {
                _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            }
            catch
            {
                try
                {
                    _capture.WaveFormat = new WaveFormat(SampleRate, 32, Channels);
                }
                catch
                {
                    _capture.WaveFormat = new WaveFormat(SampleRate, 16, Channels);
                }
            }
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.RecordingStopped += Capture_RecordingStopped;
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_capture == null)
                Initialize();

            _capture!.StartRecording();
            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_capture != null && IsCapturing)
            {
                _capture.StopRecording();
                IsCapturing = false;
            }
        }
        lock (_bufferLock)
        {
            _accumPos = 0;
        }
    }

    public static List<MMDevice> GetCaptureDevices()
    {
        var devices = new List<MMDevice>();
        using var enumerator = new MMDeviceEnumerator();
        var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        foreach (var device in collection)
        {
            devices.Add(device);
        }

        return devices;
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // 整个处理过程持有 _lock，防止 SwitchDevice 在回调执行期间释放 _capture
        lock (_lock)
        {
            if (_capture == null) return;

            // 根据实际采样格式正确解析音频数据
            int bitsPerSample = _capture.WaveFormat.BitsPerSample;
            int bytesPerSample = bitsPerSample / 8;
            int sampleCount = e.BytesRecorded / bytesPerSample;
            var samples = ArrayPool<float>.Shared.Rent(sampleCount);
            try
            {
                if (bitsPerSample == 32 && _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    // 32-bit IEEE float：直接复制
                    Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
                }
                else if (bitsPerSample == 16)
                {
                    // 16-bit PCM：转换为 float
                    for (int i = 0; i < sampleCount; i++)
                    {
                        short raw = BitConverter.ToInt16(e.Buffer, i * 2);
                        samples[i] = raw / 32768f;
                    }
                }
                else if (bitsPerSample == 32)
                {
                    // 32-bit integer PCM：转换为 float
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int raw = BitConverter.ToInt32(e.Buffer, i * 4);
                        samples[i] = raw / 2147483648f;
                    }
                }
                else
                {
                    // 不支持的格式，忽略此帧
                    return;
                }

                int outChannels = _capture.WaveFormat.Channels;

                // 音频前处理（单次遍历：RMS + 噪声门 + AGC）
                // Process 返回 RMS 值，供 InputVolume 复用，避免重复计算
                float rms = _preprocessor.Process(samples, sampleCount);
                System.Threading.Volatile.Write(ref _inputVolume, rms);

                // 声道归一：立体声 → 单声道
                int monoCount = sampleCount / outChannels;

                if (monoCount > _monoBuffer.Length)
                    _monoBuffer = new float[monoCount * 2];

                if (outChannels > 1)
                {
                    for (int i = 0; i < monoCount; i++)
                    {
                        float sum = 0;
                        for (int ch = 0; ch < outChannels; ch++)
                            sum += samples[i * outChannels + ch];
                        _monoBuffer[i] = sum / outChannels;
                    }
                }
                else
                {
                    Buffer.BlockCopy(samples, 0, _monoBuffer, 0, monoCount * 4);
                }

                // 累积缓冲（加锁保护，防止 Stop 并发重置）
                lock (_bufferLock)
                {
                    EnsureBufferCapacity(monoCount);
                    Buffer.BlockCopy(_monoBuffer, 0, _accumBuffer, _accumPos * 4, monoCount * 4);
                    _accumPos += monoCount;

                    // 提取完整帧（使用池化帧缓冲区减少 GC 压力）
                    var pooledFrame = ArrayPool<float>.Shared.Rent(FrameSize);
                    try
                    {
                        while (_accumPos >= FrameSize)
                        {
                            Buffer.BlockCopy(_accumBuffer, 0, pooledFrame, 0, FrameSize * 4);

                            int remaining = _accumPos - FrameSize;
                            if (remaining > 0)
                                Buffer.BlockCopy(_accumBuffer, FrameSize * 4, _accumBuffer, 0, remaining * 4);
                            _accumPos = remaining;

                            // 32f → 16i
                            if (_pcmBuffer.Length < FrameSize)
                                _pcmBuffer = new short[FrameSize];

                            for (int i = 0; i < FrameSize; i++)
                                _pcmBuffer[i] = (short)Math.Clamp(pooledFrame[i] * 32767f, -32768f, 32767f);

                            OnFrameReady?.Invoke(_pcmBuffer, FrameSize);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(pooledFrame);
                    }
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(samples);
            }
        }

        // 在锁外调用事件，防止回调进入 AudioCapture 方法时死锁
        InputVolumeChanged?.Invoke(InputVolume);
    }

    private void EnsureBufferCapacity(int additionalSamples)
    {
        int required = _accumPos + additionalSamples;
        if (_accumBuffer.Length < required)
        {
            int newSize = Math.Max(_accumBuffer.Length * 2, required);
            var newBuffer = new float[newSize];
            if (_accumPos > 0)
                Buffer.BlockCopy(_accumBuffer, 0, newBuffer, 0, _accumPos * 4);
            _accumBuffer = newBuffer;
        }
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
    }

    /// <summary>
    /// 切换音频设备，失败时保持旧设备
    /// </summary>
    public void SwitchDevice(MMDevice device)
    {
        lock (_lock)
        {
            var temp = new WasapiCapture(device)
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)
            };
            try
            {
                // 通过 Start/Stop 快速验证设备可用性
                temp.StartRecording();
                temp.StopRecording();
            }
            catch
            {
                temp.Dispose();
                // 保持旧设备不变
                return;
            }
            temp.DataAvailable += Capture_DataAvailable;
            temp.RecordingStopped += Capture_RecordingStopped;

            // 先取消旧设备的事件订阅，再释放（防止旧设备触发回调访问已释放对象）
            if (_capture != null)
            {
                _capture.DataAvailable -= Capture_DataAvailable;
                _capture.RecordingStopped -= Capture_RecordingStopped;
            }

            bool wasCapturing = IsCapturing;
            if (wasCapturing) _capture?.StopRecording();
            _capture?.Dispose();
            _capture = temp;

            if (wasCapturing) _capture.StartRecording();
            _accumPos = 0;
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            _capture?.Dispose();
            _capture = null;
        }
    }
}
