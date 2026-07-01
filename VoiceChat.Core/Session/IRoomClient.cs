using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;

namespace VoiceChat.Core.Session;

public interface IRoomClient : IDisposable, IAsyncDisposable
{
    bool IsConnected { get; }
    RoomInfo? CurrentRoom { get; }
    string? MemberId { get; }
    List<RoomMember> Members { get; }
    RoomMember? HostMember { get; }

    event Action<RoomInfo>? OnConnected;
    event Action? OnDisconnected;
    event Action<RoomMember>? OnMemberJoined;
    event Action<string>? OnMemberLeft;
    event Action<string, bool>? OnMemberMuteChanged;
    event Action? OnRoomDissolved;
    event Action<string>? OnUserSpeaking;
    event Action<string>? OnError;
    event Action<string>? OnStats;
    event Action<float>? OnInputVolumeChanged;

    Task<bool> ConnectAsync(RoomInfo room, string userName, string? password = null, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task MuteSelfAsync(bool mute);
    void SwitchAudioDevice(NAudio.CoreAudioApi.MMDevice device);
    void SwitchPlaybackDevice(NAudio.CoreAudioApi.MMDevice device);
    void SetUserVolume(string userId, float volume);
    void MuteOther(string memberId, bool mute);
    AudioCapture? GetAudioCapture();
}