using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

public interface ISignalingClient : IDisposable
{
    bool IsConnected { get; }
    string? MemberId { get; }
    List<RoomMember> Members { get; }
    RoomMember? HostMember { get; }

    event Action<JoinResponseData>? OnConnected;
    event Action? OnDisconnected;
    event Action<RoomMember>? OnMemberJoined;
    event Action<string>? OnMemberLeft;
    event Action<string, bool>? OnMemberMuteChanged;
    event Action? OnRoomDissolved;
    event Action<string>? OnError;

    Task<bool> ConnectAsync(string address, int port, string userName, int voicePort = 0, CancellationToken cancellationToken = default, string? password = null);
    Task DisconnectAsync();
    Task SendHeartbeatAsync();
    Task SendMuteSelfAsync(bool mute);
}