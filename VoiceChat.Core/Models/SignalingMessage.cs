using System.Text.Json;

namespace VoiceChat.Core.Models;

/// <summary>
/// 信令消息类型
/// </summary>
public enum SignalingType
{
    // 房间管理
    JoinRequest = 0x01,      // 加入请求
    JoinResponse = 0x02,     // 加入响应
    LeaveRequest = 0x03,     // 离开请求
    LeaveNotification = 0x04,// 离开通知

    // 成员同步
    MemberList = 0x10,       // 成员列表
    MemberJoined = 0x11,     // 新成员加入
    MemberLeft = 0x12,       // 成员离开
    MemberUpdated = 0x13,    // 成员信息更新

    // 语音控制
    MuteSelf = 0x20,         // 静音自己
    UnmuteSelf = 0x21,       // 取消静音自己

    // 心跳
    Heartbeat = 0x30,        // 心跳
    HeartbeatAck = 0x31,     // 心跳响应

    // 房间事件
    RoomDissolved = 0x40,    // 房间已解散（房主发送）

    // 错误
    Error = 0xFF             // 错误
}

/// <summary>
/// 信令消息
/// </summary>
public class SignalingMessage
{
    /// <summary>
    /// 消息类型
    /// </summary>
    public SignalingType Type { get; set; }

    /// <summary>
    /// 发送者ID
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// 消息数据（JSON）
    /// </summary>
    public string Data { get; set; } = "{}";

    /// <summary>
    /// 时间戳
    /// </summary>
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// 序列化消息
    /// </summary>
    public byte[] Serialize()
    {
        var json = JsonSerializer.Serialize(this);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var result = new byte[4 + bytes.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length), 0, result, 0, 4);
        Buffer.BlockCopy(bytes, 0, result, 4, bytes.Length);
        return result;
    }

    /// <summary>
    /// 从字节数组读取消息
    /// </summary>
    public static SignalingMessage? Deserialize(byte[] data)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<SignalingMessage>(json, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"反序列化失败: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// 加入请求数据
/// </summary>
public class JoinRequestData
{
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public int VoicePort { get; set; }
    public string? VoiceAddress { get; set; }
}

/// <summary>
/// 加入响应数据
/// </summary>
public class JoinResponseData
{
    public bool Success { get; set; }
    public string? MemberId { get; set; }
    public string? ErrorMessage { get; set; }
    public RoomMember? HostMember { get; set; }
    public List<RoomMember>? Members { get; set; }
}

/// <summary>
/// 成员列表数据
/// </summary>
public class MemberListData
{
    public List<RoomMember> Members { get; set; } = new();
}
