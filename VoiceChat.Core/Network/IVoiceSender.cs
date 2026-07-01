namespace VoiceChat.Core.Network;

public interface IVoiceSender : IDisposable
{
    string UserId { get; set; }
    void AddEndpoint(string memberId, System.Net.IPEndPoint endpoint);
    void RemoveEndpoint(string memberId);
    void SendVoice(byte[] data, int length);
    void SendCombinedVoice(byte[] data1, int length1, byte[] data2, int length2);
}