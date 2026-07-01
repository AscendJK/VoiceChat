using Concentus.Enums;
using Concentus.Structs;

namespace VoiceChat.Core.Audio;

#pragma warning disable CS0618

/// <summary>
/// Opus编解码器 - 直接处理16位整型 + PLC丢包补偿
/// </summary>
public class OpusCodec : IOpusCodec, IDisposable
{
    private OpusEncoder? _encoder;
    private OpusDecoder? _decoder;
    private readonly object _encoderLock = new();
    private readonly object _decoderLock = new();

    public int SampleRate { get; }
    public int Channels { get; }
    public int Bitrate { get; set; } = 64000;
    public int FrameSize { get; }
    public int MaxPacketSize => FrameSize * 4;

    public OpusCodec(int sampleRate = 48000, int channels = 1, int bitrate = 48000)
    {
        SampleRate = sampleRate;
        Channels = channels;
        Bitrate = bitrate;
        FrameSize = sampleRate * 20 / 1000;

        InitializeEncoder();
        InitializeDecoder();
    }

    private void InitializeEncoder()
    {
        _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP)
        {
            Bitrate = Bitrate,
            Complexity = 3, // 复杂度 3 对 VOIP 音质几乎无感知差异，CPU 消耗比复杂度 5 再降低 30-40%
            SignalType = OpusSignal.OPUS_SIGNAL_VOICE
        };
    }

    private void InitializeDecoder()
    {
        _decoder = new OpusDecoder(SampleRate, Channels);
    }

    /// <summary>
    /// 编码（输入16位整型单声道PCM）
    /// </summary>
    public int Encode(short[] pcm, int length, byte[] output)
    {
        lock (_encoderLock)
        {
            // null 检查在锁内，防止 Dispose 后访问已释放的编码器
            if (_encoder == null || length == 0) return 0;
            // 使用实际传入的长度（不超过 FrameSize），防止越界
            int frameLen = Math.Min(length, FrameSize);
            return _encoder.Encode(pcm, 0, frameLen, output, 0, output.Length);
        }
    }

    /// <summary>
    /// 解码（输出16位整型单声道PCM）
    /// </summary>
    public int Decode(byte[] data, int dataLength, short[] output, bool lostPacket = false)
    {
        lock (_decoderLock)
        {
            // null 检查在锁内，防止 Dispose 后访问已释放的解码器
            if (_decoder == null) return 0;
            if (lostPacket || dataLength == 0)
            {
                return _decoder.Decode(null!, 0, 0, output, 0, FrameSize);
            }
            return _decoder.Decode(data, 0, dataLength, output, 0, FrameSize);
        }
    }

    public void Dispose()
    {
        lock (_encoderLock)
        {
            _encoder?.Dispose();
            _encoder = null;
        }
        lock (_decoderLock)
        {
            _decoder?.Dispose();
            _decoder = null;
        }
        GC.SuppressFinalize(this);
    }
}

#pragma warning restore CS0618
