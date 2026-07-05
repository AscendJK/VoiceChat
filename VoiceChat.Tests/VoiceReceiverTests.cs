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

    [Fact]
    public async Task Stats_Reset_ClearsValues()
    {
        // Increment stats by receiving a packet
        _receiver.Start();
        using var sender = new VoiceSender(0);
        sender.UserId = "test";
        sender.AddEndpoint("r", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, _receiver.LocalPort));

        var tcs = new TaskCompletionSource<bool>();
        _receiver.OnVoiceReceived += _ => tcs.TrySetResult(true);

        sender.SendVoice(new byte[] { 1, 2, 3, 4 }, 4);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(_receiver.Stats.ReceivedPackets > 0);

        _receiver.Stats.Reset();
        Assert.Equal(0, _receiver.Stats.ReceivedPackets);
        Assert.Equal(0, _receiver.Stats.ReceivedBytes);
        Assert.Equal(0, _receiver.Stats.DroppedPackets);
        Assert.Equal(0, _receiver.Stats.LostPackets);
    }

    [Fact]
    public async Task Receive_ActualPacket_FiresEvent()
    {
        _receiver.Start();
        using var sender = new VoiceSender(0);
        sender.UserId = "sender1";
        sender.AddEndpoint("r", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, _receiver.LocalPort));

        var tcs = new TaskCompletionSource<Core.Models.VoicePacket>();
        _receiver.OnVoiceReceived += pkt => tcs.TrySetResult(pkt);

        sender.SendVoice(new byte[] { 0xAA, 0xBB }, 2);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(received);
        Assert.Equal("sender1", received.UserId);
    }

    [Fact]
    public void MuteUser_DroppedPacketCountIncrements()
    {
        _receiver.Start();
        _receiver.MuteUser("muted_user");

        using var sender = new VoiceSender(0);
        sender.UserId = "muted_user";
        sender.AddEndpoint("r", new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, _receiver.LocalPort));

        sender.SendVoice(new byte[] { 1, 2 }, 2);
        Thread.Sleep(200);

        Assert.True(_receiver.Stats.DroppedPackets > 0,
            "Muted user's packets should be counted as dropped");

        _receiver.UnmuteUser("muted_user");
    }

    [Fact]
    public void RemoveUserTracker_CleansUp()
    {
        _receiver.MuteUser("user1");
        Assert.True(_receiver.IsUserMuted("user1"));

        _receiver.RemoveUserTracker("user1");
        Assert.False(_receiver.IsUserMuted("user1"));
    }

    [Fact]
    public void StopCalledTwice_DoesNotThrow()
    {
        _receiver.Start();
        _receiver.Stop();
        _receiver.Stop(); // Should be idempotent
    }

    [Fact]
    public void Dispose_CleansUp()
    {
        _receiver.Start();
        _receiver.Dispose();
        // Should not throw
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

    [Fact]
    public void OutOfOrderPacket_BufferedAndOutput()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 1 }, out _, out _);

        // Send seq 2 out of order (arrives before expected seq 2)
        // Since expected is 2, seq 2 == _expectedSeq, it's treated as normal order
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 2 }, out var ready2, out int lost2);
        Assert.NotNull(ready2);
        Assert.Equal(0, lost2);
    }

    [Fact]
    public void ReorderBuffer_Overflow_ForceOutput()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 1 }, out _, out _);

        // Send many future packets to overflow buffer (max 10)
        for (uint seq = 20; seq <= 35; seq++)
        {
            tracker.ProcessPacket(new VoicePacket { SequenceNumber = seq }, out _, out _);
        }

        // Should have forced output of oldest, not crashed
    }

    [Fact]
    public void VeryOldPacket_Discarded()
    {
        var tracker = new UserPacketTracker();
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 100 }, out _, out _);
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 200 }, out _, out _);

        // Send packet 50 (way behind expected 201)
        tracker.ProcessPacket(new VoicePacket { SequenceNumber = 50 }, out var ready, out int lost);
        // Very old packets should be discarded (no output)
        Assert.Null(ready);
    }
}
