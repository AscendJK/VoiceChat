using System.Net;
using VoiceChat.Core.Network;

namespace VoiceChat.Tests;

public class VoiceSenderTests : IDisposable
{
    private readonly VoiceSender _sender;

    public VoiceSenderTests()
    {
        _sender = new VoiceSender(0);
    }

    public void Dispose()
    {
        _sender.Dispose();
    }

    [Fact]
    public void LocalPort_ReturnsValidPort()
    {
        Assert.True(_sender.LocalPort > 0 && _sender.LocalPort <= 65535);
    }

    [Fact]
    public void SendVoice_NoEndpoints_DoesNotThrow()
    {
        _sender.UserId = "user1";
        byte[] audio = new byte[] { 1, 2, 3, 4 };
        _sender.SendVoice(audio, audio.Length); // Should not throw
    }

    [Fact]
    public async Task SendVoice_WithEndpoint_SendsSuccessfully()
    {
        // Use a receiver to verify data arrives
        using var receiver = new VoiceReceiver(0);
        receiver.Start();

        var receiverEP = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        _sender.UserId = "sender1";
        _sender.AddEndpoint("receiver1", receiverEP);

        var tcs = new TaskCompletionSource<Core.Models.VoicePacket>();
        receiver.OnVoiceReceived += pkt => tcs.TrySetResult(pkt);

        byte[] audio = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        _sender.SendVoice(audio, audio.Length);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(received);
        Assert.Equal("sender1", received.UserId);
    }

    [Fact]
    public async Task SendCombinedVoice_TwoFrames_SendsBoth()
    {
        using var receiver = new VoiceReceiver(0);
        receiver.Start();

        var receiverEP = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        _sender.UserId = "sender1";
        _sender.AddEndpoint("receiver1", receiverEP);

        var receivedPackets = new List<Core.Models.VoicePacket>();
        var allReceived = new TaskCompletionSource<bool>();
        receiver.OnVoiceReceived += pkt =>
        {
            receivedPackets.Add(pkt);
            if (receivedPackets.Count >= 2) allReceived.TrySetResult(true);
        };

        byte[] audio1 = new byte[] { 0x01, 0x02 };
        byte[] audio2 = new byte[] { 0x03, 0x04 };
        _sender.SendCombinedVoice(audio1, audio1.Length, audio2, audio2.Length);

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, receivedPackets.Count);
        Assert.Equal(1u, receivedPackets[0].SequenceNumber);
        Assert.Equal(2u, receivedPackets[1].SequenceNumber);
    }

    [Fact]
    public void AddRemoveEndpoint_ManagesCorrectly()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 5000);
        _sender.AddEndpoint("user1", ep);
        _sender.RemoveEndpoint("user1");
        // No exception = pass
    }

    [Fact]
    public void ClearEndpoints_RemovesAll()
    {
        _sender.AddEndpoint("user1", new IPEndPoint(IPAddress.Loopback, 5000));
        _sender.AddEndpoint("user2", new IPEndPoint(IPAddress.Loopback, 5001));
        _sender.ClearEndpoints();
        // No exception = pass
    }

    [Fact]
    public void Stats_InitiallyZero()
    {
        Assert.Equal(0, _sender.Stats.SentPackets);
        Assert.Equal(0, _sender.Stats.SentBytes);
        Assert.Equal(0, _sender.Stats.FailedPackets);
    }

    [Fact]
    public void Stats_Reset_ClearsValues()
    {
        // Send something to increment stats
        using var receiver = new VoiceReceiver(0);
        receiver.Start();
        _sender.AddEndpoint("r", new IPEndPoint(IPAddress.Loopback, receiver.LocalPort));
        _sender.UserId = "s";
        _sender.SendVoice(new byte[] { 1 }, 1);

        Assert.True(_sender.Stats.SentPackets > 0);

        _sender.Stats.Reset();
        Assert.Equal(0, _sender.Stats.SentPackets);
        Assert.Equal(0, _sender.Stats.SentBytes);
        Assert.Equal(0, _sender.Stats.FailedPackets);

        _sender.RemoveEndpoint("r");
    }

    [Fact]
    public void IsEnabled_CanBeToggled()
    {
        _sender.IsEnabled = false;
        Assert.False(_sender.IsEnabled);

        _sender.IsEnabled = true;
        Assert.True(_sender.IsEnabled);
    }

    [Fact]
    public void SendVoice_WhenDisabled_DoesNotSend()
    {
        using var receiver = new VoiceReceiver(0);
        receiver.Start();
        _sender.AddEndpoint("r", new IPEndPoint(IPAddress.Loopback, receiver.LocalPort));
        _sender.UserId = "s";
        _sender.IsEnabled = false;

        _sender.SendVoice(new byte[] { 1 }, 1);

        Assert.Equal(0, _sender.Stats.SentPackets);
        _sender.RemoveEndpoint("r");
    }

    [Fact]
    public async Task SequenceNumber_IncrementsAcrossSends()
    {
        using var receiver = new VoiceReceiver(0);
        receiver.Start();
        _sender.AddEndpoint("r", new IPEndPoint(IPAddress.Loopback, receiver.LocalPort));
        _sender.UserId = "s";

        var packets = new List<Core.Models.VoicePacket>();
        var tcs = new TaskCompletionSource<bool>();
        receiver.OnVoiceReceived += pkt =>
        {
            packets.Add(pkt);
            if (packets.Count >= 3) tcs.TrySetResult(true);
        };

        _sender.SendVoice(new byte[] { 1 }, 1);
        _sender.SendVoice(new byte[] { 2 }, 1);
        _sender.SendVoice(new byte[] { 3 }, 1);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, packets.Count);
        Assert.Equal(1u, packets[0].SequenceNumber);
        Assert.Equal(2u, packets[1].SequenceNumber);
        Assert.Equal(3u, packets[2].SequenceNumber);

        _sender.RemoveEndpoint("r");
    }
}
