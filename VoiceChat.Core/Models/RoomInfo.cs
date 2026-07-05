using System.Net;

namespace VoiceChat.Core.Models;

/// <summary>
/// 房间信息
/// </summary>
public class RoomInfo
{
    /// <summary>
    /// 房间ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 房间名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 房主名称
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// 房主IP地址
    /// </summary>
    public string HostAddress { get; set; } = string.Empty;

    /// <summary>
    /// 信令服务器端口
    /// </summary>
    public int SignalingPort { get; set; }

    /// <summary>
    /// 语音传输端口
    /// </summary>
    public int VoicePort { get; set; }

    /// <summary>
    /// 当前成员数量
    /// </summary>
    public int MemberCount { get; set; }

    /// <summary>
    /// 最大成员数量
    /// </summary>
    public int MaxMembers { get; set; } = 20;

    /// <summary>
    /// 是否需要密码
    /// </summary>
    public bool HasPassword { get; set; }

    /// <summary>
    /// 语音质量配置
    /// </summary>
    public VoiceQuality Quality { get; set; } = VoiceQuality.Standard;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后广播时间
    /// </summary>
    public DateTime LastBroadcastTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 获取房主终结点
    /// </summary>
    public IPEndPoint GetHostEndPoint() => new(IPAddress.Parse(HostAddress), SignalingPort);

    public override string ToString() => $"{Name} ({HostName}) - {MemberCount}人在线";

    /// <summary>
    /// 原地更新属性（保持对象引用不变，避免UI绑定丢失）
    /// </summary>
    public void UpdateFrom(RoomInfo other)
    {
        Name = other.Name;
        HostName = other.HostName;
        HostAddress = other.HostAddress;
        SignalingPort = other.SignalingPort;
        VoicePort = other.VoicePort;
        MemberCount = other.MemberCount;
        MaxMembers = other.MaxMembers;
        HasPassword = other.HasPassword;
        Quality = other.Quality;
        LastBroadcastTime = other.LastBroadcastTime;
    }
}
