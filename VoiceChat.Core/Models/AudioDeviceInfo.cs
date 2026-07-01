namespace VoiceChat.Core.Models;

/// <summary>
/// 音频设备信息
/// </summary>
public class AudioDeviceInfo
{
    /// <summary>
    /// 设备ID
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 设备友好名称
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为默认设备
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 设备类型
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public override string ToString() => $"{(IsDefault ? "[默认] " : "")}{FriendlyName}";
}
