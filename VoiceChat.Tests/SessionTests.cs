using VoiceChat.Core.Models;

namespace VoiceChat.Tests;

public class SessionTests
{
    [Fact]
    public void VoiceQuality_Standard_HasCorrectValues()
    {
        var q = VoiceQuality.Standard;
        Assert.Equal(48000, q.SampleRate);
        Assert.Equal(1, q.Channels);
        Assert.Equal(20, q.FrameSizeMs);
        Assert.Equal(64000, q.Bitrate);
        Assert.Equal(960, q.FrameSize);
    }

    [Fact]
    public void VoiceQuality_HighDefinition_HasCorrectValues()
    {
        var q = VoiceQuality.HighDefinition;
        Assert.Equal(48000, q.SampleRate);
        Assert.Equal(1, q.Channels);
        Assert.Equal(96000, q.Bitrate);
    }

    [Fact]
    public void RoomInfo_ToString_ContainsNameAndHost()
    {
        var info = new RoomInfo
        {
            Name = "TestRoom",
            HostName = "Alice",
            MemberCount = 3
        };

        string str = info.ToString();
        Assert.Contains("TestRoom", str);
        Assert.Contains("Alice", str);
        Assert.Contains("3", str);
    }

    [Fact]
    public void RoomInfo_UpdateFrom_PreservesReference()
    {
        var original = new RoomInfo { Name = "Original", MemberCount = 1 };
        var updated = new RoomInfo { Name = "Updated", MemberCount = 5 };

        var reference = original;
        original.UpdateFrom(updated);

        Assert.Same(reference, original);
        Assert.Equal("Updated", original.Name);
        Assert.Equal(5, original.MemberCount);
    }

    [Fact]
    public void RoomMember_DefaultValues_AreSane()
    {
        var member = new RoomMember
        {
            Id = "test-id",
            Name = "TestUser"
        };

        Assert.Equal(1.0f, member.Volume);
        Assert.False(member.IsMuted);
        Assert.False(member.IsMutedByMe);
        Assert.False(member.IsSpeaking);
        Assert.False(member.IsHost);
    }

    [Fact]
    public void JoinRequestData_Serialize_Roundtrip()
    {
        var data = new JoinRequestData
        {
            UserName = "TestUser",
            Password = "secret",
            VoicePort = 12345
        };

        string json = System.Text.Json.JsonSerializer.Serialize(data);
        var restored = System.Text.Json.JsonSerializer.Deserialize<JoinRequestData>(json);

        Assert.NotNull(restored);
        Assert.Equal(data.UserName, restored.UserName);
        Assert.Equal(data.VoicePort, restored.VoicePort);
    }

    [Fact]
    public void JoinResponseData_Success_ParsedCorrectly()
    {
        var data = new JoinResponseData
        {
            Success = true,
            MemberId = "member-123",
            ErrorMessage = null,
            Members = new List<RoomMember>
            {
                new() { Id = "m1", Name = "Alice" },
                new() { Id = "m2", Name = "Bob" }
            }
        };

        string json = System.Text.Json.JsonSerializer.Serialize(data);
        var restored = System.Text.Json.JsonSerializer.Deserialize<JoinResponseData>(json);

        Assert.NotNull(restored);
        Assert.True(restored.Success);
        Assert.Equal("member-123", restored.MemberId);
        Assert.Equal(2, restored.Members?.Count ?? 0);
    }

    [Fact]
    public void SignalingMessage_SerializeDeserialize_Roundtrip()
    {
        var msg = new SignalingMessage
        {
            Type = SignalingType.JoinRequest,
            SenderId = "sender-123",
            Data = "{\"userName\":\"test\"}",
            Timestamp = 1234567890L
        };

        // Serialize 包含 4 字节长度前缀 + JSON 数据
        byte[] bytes = msg.Serialize();

        // 解析时需要跳过长度前缀
        int length = BitConverter.ToInt32(bytes, 0);
        byte[] jsonBytes = new byte[length];
        Buffer.BlockCopy(bytes, 4, jsonBytes, 0, length);
        var restored = SignalingMessage.Deserialize(jsonBytes);

        Assert.NotNull(restored);
        Assert.Equal(msg.Type, restored.Type);
        Assert.Equal(msg.SenderId, restored.SenderId);
        Assert.Equal(msg.Data, restored.Data);
        Assert.Equal(msg.Timestamp, restored.Timestamp);
    }
}
