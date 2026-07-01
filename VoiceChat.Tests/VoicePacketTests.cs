using VoiceChat.Core.Models;

namespace VoiceChat.Tests;

public class VoicePacketTests
{
    [Fact]
    public void SerializeDeserialize_Roundtrip_PreservesData()
    {
        var packet = new VoicePacket
        {
            PacketType = 0x01,
            UserId = "user123",
            Timestamp = 1234567890L,
            SequenceNumber = 42,
            AudioData = new byte[] { 1, 2, 3, 4, 5 },
            AudioDataLength = 5
        };

        byte[] data = packet.Serialize();
        var restored = VoicePacket.Deserialize(data);

        Assert.NotNull(restored);
        Assert.Equal(packet.PacketType, restored.PacketType);
        Assert.Equal(packet.UserId, restored.UserId);
        Assert.Equal(packet.Timestamp, restored.Timestamp);
        Assert.Equal(packet.SequenceNumber, restored.SequenceNumber);
        Assert.Equal(packet.AudioDataLength, restored.AudioDataLength);
        Assert.Equal(packet.AudioData, restored.AudioData);
    }

    [Fact]
    public void SerializeDeserialize_EmptyAudio_Works()
    {
        var packet = new VoicePacket
        {
            UserId = "test",
            AudioDataLength = 0
        };

        byte[] data = packet.Serialize();
        var restored = VoicePacket.Deserialize(data);

        Assert.NotNull(restored);
        Assert.Equal(0, restored.AudioDataLength);
    }

    [Fact]
    public void SerializeDeserialize_CjkUserId_Works()
    {
        var packet = new VoicePacket
        {
            UserId = "张三的房间用户",
            AudioData = new byte[10],
            AudioDataLength = 10
        };

        byte[] data = packet.Serialize();
        var restored = VoicePacket.Deserialize(data);

        Assert.NotNull(restored);
        Assert.Equal(packet.UserId, restored.UserId);
    }

    [Fact]
    public void SerializeDeserialize_LongUserId_Works()
    {
        var packet = new VoicePacket
        {
            UserId = new string('A', 200),
            AudioDataLength = 0
        };

        byte[] data = packet.Serialize();
        var restored = VoicePacket.Deserialize(data);

        Assert.NotNull(restored);
        Assert.Equal(200, restored.UserId.Length);
    }

    [Fact]
    public void DeserializeMultiple_TwoPackets_BothParsed()
    {
        var packet1 = new VoicePacket { UserId = "user1", SequenceNumber = 1, AudioData = new byte[] { 1, 2 }, AudioDataLength = 2 };
        var packet2 = new VoicePacket { UserId = "user2", SequenceNumber = 2, AudioData = new byte[] { 3, 4 }, AudioDataLength = 2 };

        byte[] combined = packet1.Serialize().Concat(packet2.Serialize()).ToArray();
        var packets = VoicePacket.DeserializeMultiple(combined);

        Assert.Equal(2, packets.Count);
        Assert.Equal("user1", packets[0].UserId);
        Assert.Equal("user2", packets[1].UserId);
    }

    [Fact]
    public void Deserialize_CorruptedData_ReturnsNull()
    {
        byte[] corrupted = new byte[] { 0x01, 0xFF, 0xFF, 0xFF };
        var result = VoicePacket.Deserialize(corrupted);
        Assert.Null(result);
    }

    [Fact]
    public void DeserializeMultiple_MaxCount_LimitsAllocation()
    {
        // Create a byte sequence that would parse as many tiny packets
        var data = new List<byte>();
        for (int i = 0; i < 200; i++)
        {
            data.Add(0x01); // PacketType
            data.Add(0x00); // UserIdLength low (0)
            data.Add(0x00); // UserIdLength high (0)
            data.AddRange(BitConverter.GetBytes(0L)); // Timestamp
            data.AddRange(BitConverter.GetBytes((uint)i)); // SequenceNumber
            data.AddRange(BitConverter.GetBytes(0)); // AudioDataLength
        }

        var packets = VoicePacket.DeserializeMultiple(data.ToArray());
        Assert.True(packets.Count <= 10, $"Expected max 10 packets, got {packets.Count}");
    }

    [Fact]
    public void Serialize_LargeUserId_TruncatedGracefully()
    {
        // UserID > 65535 UTF-8 bytes should be truncated
        var packet = new VoicePacket
        {
            UserId = new string('中', 30000), // Each '中' is 3 UTF-8 bytes = ~90000 bytes
            AudioDataLength = 0
        };

        byte[] data = packet.Serialize();
        var restored = VoicePacket.Deserialize(data);

        Assert.NotNull(restored);
        Assert.True(restored.UserId.Length <= 32);
    }
}
