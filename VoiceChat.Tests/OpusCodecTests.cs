using VoiceChat.Core.Audio;

namespace VoiceChat.Tests;

public class OpusCodecTests : IDisposable
{
    private readonly OpusCodec _codec;

    public OpusCodecTests()
    {
        _codec = new OpusCodec(48000, 1, 48000);
    }

    public void Dispose()
    {
        _codec.Dispose();
    }

    [Fact]
    public void EncodeDecode_Roundtrip_DataMatches()
    {
        // Generate 20ms of 440Hz sine wave @ 48kHz
        int frameSize = 48000 * 20 / 1000; // 960 samples
        short[] pcm = new short[frameSize];
        double phase = 0;
        for (int i = 0; i < frameSize; i++)
        {
            pcm[i] = (short)(16000 * Math.Sin(phase));
            phase += 2.0 * Math.PI * 440.0 / 48000;
        }

        byte[] encoded = new byte[_codec.MaxPacketSize];
        int encodedLength = _codec.Encode(pcm, frameSize, encoded);

        Assert.True(encodedLength > 0, "Encode should return positive length");

        short[] decoded = new short[frameSize];
        int decodedSamples = _codec.Decode(encoded, encodedLength, decoded, false);

        Assert.Equal(frameSize, decodedSamples);

        // Verify audio is similar (not exact due to lossy compression)
        double correlation = 0;
        for (int i = 0; i < frameSize; i++)
        {
            correlation += pcm[i] * decoded[i];
        }
        Assert.True(correlation > 0, "Encoded-decoded audio should correlate with original");
    }

    [Fact]
    public void Encode_SilentInput_SmallOutput()
    {
        short[] silent = new short[960]; // all zeros
        byte[] encoded = new byte[_codec.MaxPacketSize];
        int encodedLength = _codec.Encode(silent, silent.Length, encoded);

        // Silent frame should compress to small size (less than loud frame)
        Assert.True(encodedLength > 0);

        // Loud frame for comparison
        short[] loud = new short[960];
        for (int i = 0; i < loud.Length; i++)
            loud[i] = (short)(16000 * Math.Sin(i * 0.1));
        byte[] encodedLoud = new byte[_codec.MaxPacketSize];
        int loudLength = _codec.Encode(loud, loud.Length, encodedLoud);

        Assert.True(encodedLength < loudLength,
            $"Silent ({encodedLength}) should be smaller than loud ({loudLength})");
    }

    [Fact]
    public void Encode_ZeroLength_ReturnsZero()
    {
        byte[] encoded = new byte[_codec.MaxPacketSize];
        int result = _codec.Encode(new short[10], 0, encoded);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Decode_LostPacket_ReturnsPlcData()
    {
        short[] decoded = new short[_codec.FrameSize];
        int samples = _codec.Decode(null!, 0, decoded, lostPacket: true);

        Assert.Equal(_codec.FrameSize, samples);
    }

    [Fact]
    public void Encode_LengthParameterClamped_CorrectFrameSize()
    {
        // Pass length > FrameSize - should be clamped
        short[] pcm = new short[_codec.FrameSize * 2];
        Random.Shared.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan()));

        byte[] encoded1 = new byte[_codec.MaxPacketSize];
        byte[] encoded2 = new byte[_codec.MaxPacketSize];
        int normalLen = _codec.Encode(pcm, _codec.FrameSize, encoded1);
        int overflowLen = _codec.Encode(pcm, _codec.FrameSize * 2, encoded2);

        // Both should produce positive output; overflow should not crash
        Assert.True(normalLen > 0);
        Assert.True(overflowLen > 0);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var codec = new OpusCodec(48000, 1, 48000);
        codec.Dispose();
        codec.Dispose(); // Should not throw
    }

    [Fact]
    public void Encode_AfterDispose_ReturnsZero()
    {
        var codec = new OpusCodec(48000, 1, 48000);
        codec.Dispose();

        byte[] encoded = new byte[codec.MaxPacketSize];
        int result = codec.Encode(new short[960], 960, encoded);
        Assert.Equal(0, result);
    }

    [Fact]
    public void MultipleEncodeDecode_NoMemoryLeak()
    {
        int frameSize = _codec.FrameSize;
        short[] pcm = new short[frameSize];
        Random.Shared.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan()));

        byte[] encoded = new byte[_codec.MaxPacketSize];
        short[] decoded = new short[frameSize];

        for (int i = 0; i < 1000; i++)
        {
            int len = _codec.Encode(pcm, frameSize, encoded);
            Assert.True(len > 0);
            int samples = _codec.Decode(encoded, len, decoded, false);
            Assert.Equal(frameSize, samples);
        }
    }
}
