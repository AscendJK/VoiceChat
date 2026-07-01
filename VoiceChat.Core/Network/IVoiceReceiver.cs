using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

public interface IVoiceReceiver : IDisposable
{
    bool IsReceiving { get; }
    int LocalPort { get; }
    VoiceReceiveStats Stats { get; }

    event Action<VoicePacket>? OnVoiceReceived;
    event Action<string, int>? OnPacketsLost;

    void Start();
    void Stop();
    void MuteUser(string userId);
    void UnmuteUser(string userId);
    bool IsUserMuted(string userId);
    void RemoveUserTracker(string userId);
}