using VoiceChat.Core.Models;
using VoiceChat.Core.Network;
using System.Collections.Concurrent;

namespace VoiceChat.Tests;

public class VoiceReceiverTests : IDisposable
{
    private readonly VoiceReceiver _receiver;

    public VoiceReceiverTests()
    {
        _receiver = new VoiceReceiver(0);
    }

    public void Dispose()
    {
        _receiver.Stop();
        _receiver.Dispose();
    }

    [Fact]
    public void StartStop_Works()
    {
        Assert.True(_receiver.LocalPort > 0);
        _receiver.Start();
        _receiver.Stop();
        // Should not throw
    }

    [Fact]
    public void StartCalledTwice_DoesNotFail()
    {
        _receiver.Start();
        _receiver.Start(); // Should be idempotent
    }

    [Fact]
    public void LocalPort_AlwaysReturnsValid()
    {
        int port = _receiver.LocalPort;
        Assert.True(port > 0 && port <= 65535);
    }

    [Fact]
    public void MuteUser_FiltersPackets()
    {
        _receiver.MuteUser("user1");
        Assert.True(_receiver.IsUserMuted("user1"));
        Assert.False(_receiver.IsUserMuted("user2"));

        _receiver.UnmuteUser("user1");
        Assert.False(_receiver.IsUserMuted("user1"));
    }

    [Fact]
    public void Stats_InitiallyZero()
    {
        Assert.Equal(0, _receiver.Stats.ReceivedPackets);
        Assert.Equal(0, _receiver.Stats.LostPackets);
        Assert.Equal(0, _receiver.Stats.PacketLossRate);
    }
}

public class UserPacketTrackerTests
{
    [Fact]
    public void FirstPacket_EmittedImmediately()
    {
        var tracker = new UserPacketTracker();
        var packet = new VoicePacket { SequenceNumber = 100 };
        tracker.ProcessPacket(packet, out var ready, out int lost);

        Assert.NotNull(ready);
        Assert.Equal(0, lost);
    }

    [Fact]
    public void InOrderPackets_EmittedImmediately()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 1 }, out _, out _);

        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 2 }, out var ready2, out int lost2);
        Assert.NotNull(ready2);
        Assert.Equal(0, lost2);
    }

    [Fact]
    public void FuturePacket_ReportsLoss()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 1 }, out _, out _);

        // Skip to seq 5 (2,3,4 are lost)
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 5 }, out var ready, out int lost);
        Assert.NotNull(ready);
        Assert.Equal(3, lost);
    }

    [Fact]
    public void DuplicatePacket_Handled()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 1 }, out _, out _);
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 1 }, out var ready, out int lost);

        // Duplicate should still emit (no crash)
        Assert.NotNull(ready);
    }

    [Fact]
    public void Wraparound_HandledGracefully()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = uint.MaxValue - 1 }, out _, out _);

        tracker.ProcessPacket(new VoicePacket { SequenceNumber = uint.MaxValue }, out var ready1, out int lost1);
        Assert.NotNull(ready1);

        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 0 }, out var ready2, out int lost2);
        Assert.NotNull(ready2);
    }
}
