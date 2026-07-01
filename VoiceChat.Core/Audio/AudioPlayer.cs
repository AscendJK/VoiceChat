using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;

namespace VoiceChat.Core.Audio;

/// <summary>
/// 音频播放器 - 确保采样率完全对齐
/// </summary>
public class AudioPlayer : IAudioPlayer, IDisposable
{
    private WasapiOut? _output;
    private WaveFormat? _waveFormat;
    private MixingSampleProvider? _mixer;
    private readonly ConcurrentDictionary<string, BufferedWaveProvider> _userBuffers = new();
    private readonly ConcurrentDictionary<string, ISampleProvider> _userSampleProviders = new();
    private readonly ConcurrentDictionary<string, float> _userVolumes = new();
    private readonly object _lock = new();

    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 1;
    public bool IsPlaying { get; private set; }

    public void Initialize(MMDevice? device = null)
    {
        lock (_lock)
        {
            // 混音器必须使用IEEE float格式
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

            // 创建混音器
            _mixer = new MixingSampleProvider(_waveFormat);
            _mixer.ReadFully = true;

            _output = device == null
                ? new WasapiOut()
                : new WasapiOut(device, AudioClientShareMode.Shared, false, 100);

            _output.Init(_mixer);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_output == null)
                Initialize();

            _output!.Play();
            IsPlaying = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_output != null && IsPlaying)
            {
                _output.Stop();
                IsPlaying = false;
            }
        }
    }

    /// <summary>
    /// 设置用户音量
    /// </summary>
    public void SetUserVolume(string userId, float volume)
    {
        _userVolumes[userId] = Math.Clamp(volume, 0f, 2f);
    }

    // 预分配编码缓冲区，避免每帧从 ArrayPool Rent/Return
    private byte[] _encodeBuffer = new byte[1920]; // 960 samples * 2 bytes * 2 (双声道安全)

    public void AddAudioData(string userId, short[] pcm, int count)
    {
        if (!IsPlaying || count == 0) return;

        // 懒初始化用户缓冲区（加锁防止 GetOrAdd 工厂多次执行导致重复 mixer 输入）
        if (!_userBuffers.TryGetValue(userId, out var buffer))
        {
            lock (_lock)
            {
                if (!_userBuffers.TryGetValue(userId, out buffer))
                {
                    var pcmFormat = new WaveFormat(SampleRate, 16, Channels);
                    buffer = new BufferedWaveProvider(pcmFormat)
                    {
                        BufferLength = pcmFormat.AverageBytesPerSecond * 3,
                        DiscardOnBufferOverflow = true
                    };
                    var sampleProvider = buffer.ToSampleProvider();
                    _userSampleProviders[userId] = sampleProvider;
                    _mixer?.AddMixerInput(sampleProvider);
                    _userBuffers[userId] = buffer;
                }
            }
        }

        // 应用用户音量（限幅防止削波），不修改输入数组
        float volume = _userVolumes.GetOrAdd(userId, 1f);
        int byteCount = count * 2;

        // 确保缓冲区足够大
        if (_encodeBuffer.Length < byteCount)
        {
            _encodeBuffer = new byte[byteCount * 2];
        }

        // 直接使用预分配缓冲区，避免 ArrayPool 开销
        for (int i = 0; i < count; i++)
        {
            float sample = pcm[i] * volume;
            short clamped = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
            _encodeBuffer[i * 2] = (byte)(clamped & 0xFF);
            _encodeBuffer[i * 2 + 1] = (byte)((clamped >> 8) & 0xFF);
        }

        buffer.AddSamples(_encodeBuffer, 0, byteCount);
    }

    public void RemoveUser(string userId)
    {
        lock (_lock)
        {
            _userBuffers.TryRemove(userId, out _);
            _userVolumes.TryRemove(userId, out _);
            if (_userSampleProviders.TryRemove(userId, out var provider))
                _mixer?.RemoveMixerInput(provider);
        }
    }

    public void ClearUsers()
    {
        lock (_lock)
        {
            foreach (var provider in _userSampleProviders.Values)
                _mixer?.RemoveMixerInput(provider);
            _userBuffers.Clear();
            _userSampleProviders.Clear();
        }
    }

    /// <summary>
    /// 切换播放设备（不重启应用），失败时保持旧设备
    /// </summary>
    public void SwitchPlaybackDevice(MMDevice device)
    {
        lock (_lock)
        {
            if (_mixer == null) return;

            // 直接尝试用混音器初始化新设备
            var temp = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
            try
            {
                temp.Init(_mixer);
            }
            catch
            {
                temp.Dispose();
                return; // 保持旧设备不变
            }
            temp.PlaybackStopped += (s, a) => IsPlaying = false;

            // 新设备就绪，安全切换
            bool wasPlaying = IsPlaying;
            if (wasPlaying) _output?.Stop();
            _output?.Dispose();
            _output = temp;

            if (wasPlaying) _output.Play();
            IsPlaying = wasPlaying;
        }
    }

    public static List<MMDevice> GetPlaybackDevices()
    {
        var devices = new List<MMDevice>();
        using var enumerator = new MMDeviceEnumerator();
        var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in collection)
        {
            devices.Add(device);
        }

        return devices;
    }

    public void Dispose()
    {
        Stop();
        ClearUsers();
        lock (_lock)
        {
            _output?.Dispose();
            _output = null;
        }
    }
}
