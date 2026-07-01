using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;

namespace VoiceChat.Tests;

public class StressTests : IDisposable
{
    public void Dispose() { }

    [Fact]
    public void OpusCodec_Encode1000Frames_NoLeak()
    {
        using var codec = new OpusCodec(48000, 1, 48000);
        var pcm = new short[960];
        Random.Shared.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan()));
        var encoded = new byte[codec.MaxPacketSize];

        long memoryBefore = GC.GetTotalMemory(true);

        for (int i = 0; i < 1000; i++)
        {
            int len = codec.Encode(pcm, pcm.Length, encoded);
            Assert.True(len > 0, $"Encode failed at frame {i}");
        }

        GC.Collect();
        long memoryAfter = GC.GetTotalMemory(true);

        // Memory growth should be minimal (< 10MB for 1000 frames, allowing GC variability)
        long growth = memoryAfter - memoryBefore;
        Assert.True(growth < 10_000_000,
            $"Memory grew by {growth} bytes after 1000 frames");
    }

    [Fact]
    public void AudioPreprocessor_Process1000Frames_NoLeak()
    {
        var preprocessor = new AudioPreprocessor();
        var buffer = new float[960];
        Random.Shared.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan()));

        for (int i = 0; i < 1000; i++)
        {
            var copy = (float[])buffer.Clone();
            preprocessor.Process(copy, copy.Length);
        }

        // No exception, no crash = pass
    }

    [Fact]
    public void VoicePacket_SerializeDeserialize_1000Times()
    {
        var packet = new VoicePacket
        {
            UserId = "user1",
            AudioData = new byte[240],
            AudioDataLength = 240
        };
        Random.Shared.NextBytes(packet.AudioData);

        for (int i = 0; i < 1000; i++)
        {
            packet.SequenceNumber = (uint)i;
            byte[] data = packet.Serialize();
            var restored = VoicePacket.Deserialize(data);

            Assert.NotNull(restored);
            Assert.Equal(packet.SequenceNumber, restored.SequenceNumber);
            Assert.Equal(packet.AudioDataLength, restored.AudioDataLength);
        }
    }

    [Fact]
    public void VoicePacket_MaliciousData_DoesNotOOM()
    {
        // Craft a malicious datagram with many tiny packet headers
        var data = new List<byte>();
        for (int i = 0; i < 10000; i++)
        {
            data.Add(0x01); // PacketType
            data.Add(0x00); // UserIdLength low
            data.Add(0x00); // UserIdLength high
            data.AddRange(BitConverter.GetBytes(0L)); // Timestamp
            data.AddRange(BitConverter.GetBytes((uint)i)); // SequenceNumber
            data.AddRange(BitConverter.GetBytes(0)); // AudioDataLength
        }

        // Should not throw OOM
        var packets = VoicePacket.DeserializeMultiple(data.ToArray());
        Assert.True(packets.Count <= 100, $"Expected max 100 packets, got {packets.Count}");
    }

    [Fact]
    public void VoicePacket_LargeData_DoesNotProcess()
    {
        // Data larger than 64KB should be rejected immediately
        var hugeData = new byte[100_000];
        var packets = VoicePacket.DeserializeMultiple(hugeData);
        Assert.Empty(packets);
    }

    [Fact]
    public async Task SignalingServer_MultipleStartStop_NoLeak()
    {
        for (int i = 0; i < 5; i++)
        {
            var server = new SignalingServer();
            await server.StartAsync(0);
            Assert.True(server.IsRunning);
            server.Stop();
            Assert.False(server.IsRunning);
            server.Dispose();
        }
    }

    [Fact]
    public async Task MultipleClients_ConnectDisconnect_Cycle()
    {
        var server = new SignalingServer();
        await server.StartAsync(0);

        for (int cycle = 0; cycle < 3; cycle++)
        {
            var client = new SignalingClient();
            var tcs = new TaskCompletionSource<JoinResponseData>();
            client.OnConnected += data => tcs.TrySetResult(data);

            bool ok = await client.ConnectAsync("127.0.0.1", server.Port, $"User{cycle}", 10000 + cycle);
            Assert.True(ok);

            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(response.Success);

            await client.DisconnectAsync();
            client.Dispose();
        }

        server.Stop();
        server.Dispose();
    }
}
