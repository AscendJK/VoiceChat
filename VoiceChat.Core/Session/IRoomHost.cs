using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;

namespace VoiceChat.Core.Session;

public interface IRoomHost : IDisposable, IAsyncDisposable
{
    bool IsRunning { get; }
    RoomInfo RoomInfo { get; }
    RoomMember HostMember { get; }
    List<RoomMember> Members { get; }

    event Action<RoomMember>? OnMemberJoined;
    event Action<string>? OnMemberLeft;
    event Action<string, bool>? OnMemberMuteChanged;
    event Action<string>? OnUserSpeaking;
    event Action<string>? OnError;
    event Action<string>? OnStats;
    event Action<float>? OnInputVolumeChanged;

    Task<bool> CreateAsync(string roomName, string hostName, int port = 0,
        string? password = null, VoiceQuality? quality = null);
    Task CloseAsync();
    Task KickMemberAsync(string memberId);
    void MuteMember(string memberId, bool mute);
    void MuteSelf(bool mute);
    AudioCapture? GetAudioCapture();
    void SwitchAudioDevice(NAudio.CoreAudioApi.MMDevice device);
    void SwitchPlaybackDevice(NAudio.CoreAudioApi.MMDevice device);
}