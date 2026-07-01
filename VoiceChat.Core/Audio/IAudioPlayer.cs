using NAudio.CoreAudioApi;

namespace VoiceChat.Core.Audio;

public interface IAudioPlayer : IDisposable
{
    int SampleRate { get; set; }
    int Channels { get; set; }

    void Initialize(NAudio.CoreAudioApi.MMDevice? device = null);
    void Start();
    void Stop();
    void AddAudioData(string userId, short[] pcm, int count);
    void RemoveUser(string userId);
    void SetUserVolume(string userId, float volume);
    void SwitchPlaybackDevice(MMDevice device);
}