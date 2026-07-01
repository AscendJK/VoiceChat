using System.Net;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

public interface ISignalingServer : IDisposable
{
    int Port { get; }
    bool IsRunning { get; }

    event Action<RoomMember, IPEndPoint>? OnMemberJoin;
    event Action<string>? OnMemberLeave;
    event Action<string, bool>? OnMemberMuteChanged;
    event Action<string, IPEndPoint>? OnVoiceEndpointRegistered;

    void SetHostMember(RoomMember hostMember);
    void SetPassword(string? password);
    Task StartAsync(int port = 0);
    void Stop();
    Task BroadcastAsync(SignalingMessage message, string? excludeId = null);
    Task BroadcastRoomDissolvedAsync();
    Task KickMemberAsync(string connectionId);
}