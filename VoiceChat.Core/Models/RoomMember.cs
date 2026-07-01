using System.Net;
using System.Text.Json.Serialization;

namespace VoiceChat.Core.Models;

/// <summary>
/// 房间成员
/// </summary>
public class RoomMember
{
    /// <summary>
    /// 成员ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 成员名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 语音终结点（不序列化，通过事件单独传递）
    /// </summary>
    [JsonIgnore]
    public IPEndPoint? VoiceEndPoint { get; set; }

    /// <summary>
    /// 语音IP地址（用于序列化）
    /// </summary>
    public string? VoiceAddress { get; set; }

    /// <summary>
    /// 语音端口（用于序列化）
    /// </summary>
    public int VoicePort { get; set; }

    /// <summary>
    /// 信令连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 是否是房主
    /// </summary>
    public bool IsHost { get; set; }

    /// <summary>
    /// 是否静音自己（不发送语音）
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// 是否被我静音（不播放该用户语音）
    /// </summary>
    public bool IsMutedByMe { get; set; }

    /// <summary>
    /// 是否正在说话
    /// </summary>
    public bool IsSpeaking { get; set; }

    /// <summary>
    /// 该用户的音量（0.0 ~ 1.0）
    /// </summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// 加入时间
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 延迟（毫秒）
    /// </summary>
    public int Latency { get; set; }

    public override string ToString() => $"{Name} {(IsHost ? "(房主)" : "")} {(IsMuted ? "[已静音]" : "")}";
}
