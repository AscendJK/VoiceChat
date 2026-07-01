namespace VoiceChat.Core.Audio;

public interface IOpusCodec : IDisposable
{
    int FrameSize { get; }
    int MaxPacketSize { get; }
    int Encode(short[] pcm, int length, byte[] output);
    int Decode(byte[] data, int dataLength, short[] output, bool lostPacket = false);
}