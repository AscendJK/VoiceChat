namespace VoiceChat.Core.Models;

/// <summary>
/// 语音质量配置
/// </summary>
public class VoiceQuality
{
    /// <summary>
    /// 采样率 (Hz)
    /// </summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>
    /// 码率 (bps)
    /// </summary>
    public int Bitrate { get; set; } = 128000;

    /// <summary>
    /// 帧长 (ms)
    /// </summary>
    public int FrameSizeMs { get; set; } = 20;

    /// <summary>
    /// 声道数
    /// </summary>
    public int Channels { get; set; } = 1;

    /// <summary>
    /// 标准语音模式：48kHz + 64kbps（FM 广播级）
    /// </summary>
    public static VoiceQuality Standard => new()
    {
        SampleRate = 48000,
        Bitrate = 64000,
        FrameSizeMs = 20,
        Channels = 1
    };

    /// <summary>
    /// 高清语音模式：48kHz + 96kbps（优秀语音）
    /// </summary>
    public static VoiceQuality HighDefinition => new()
    {
        SampleRate = 48000,
        Bitrate = 96000,
        FrameSizeMs = 20,
        Channels = 1
    };

    /// <summary>
    /// 超清语音模式：48kHz + 128kbps（人耳透明）
    /// </summary>
    public static VoiceQuality UltraHigh => new()
    {
        SampleRate = 48000,
        Bitrate = 128000,
        FrameSizeMs = 20,
        Channels = 1
    };

    /// <summary>
    /// 每帧采样数
    /// </summary>
    public int FrameSize => SampleRate * FrameSizeMs / 1000;

    public override string ToString() => $"{SampleRate / 1000}kHz/{Bitrate / 1000}kbps";
}
