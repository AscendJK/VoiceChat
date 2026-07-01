using NAudio.CoreAudioApi;

namespace VoiceChat.Core.Audio;

public interface IAudioCapture : IDisposable
{
    int SampleRate { get; set; }
    int Channels { get; set; }
    int FrameSizeMs { get; set; }
    float InputVolume { get; }

    event Action<short[], int>? OnFrameReady;
    event Action<float>? InputVolumeChanged;

    void Initialize(MMDevice? device = null);
    void Start();
    void Stop();
    void SwitchDevice(MMDevice device);
    AudioPreprocessor GetPreprocessor();
}