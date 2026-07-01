using VoiceChat.Core.Models;
using VoiceChat.Core.Network;
using VoiceChat.Core.Session;

namespace VoiceChat.Tests;

public class RoomClientTests : IAsyncLifetime
{
    private SignalingServer? _server;
    private RoomClient? _client;

    public async Task InitializeAsync()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);
    }

    public async Task DisposeAsync()
    {
        try { _client?.Dispose(); } catch { }
        try { _server?.Stop(); } catch { }
    }

    [Fact]
    public async Task ConnectAsync_ValidServer_Succeeds()
    {
        var serverTcs = new TaskCompletionSource<RoomMember>();
        _server!.OnMemberJoin += (member, ep) => serverTcs.TrySetResult(member);

        _client = new RoomClient();
        var clientTcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => clientTcs.TrySetResult(room);

        bool ok = await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server.Port,
            VoicePort = 12345,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        Assert.True(ok);
        Assert.NotNull(_client.MemberId);

        var roomInfo = await clientTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(roomInfo);
    }

    [Fact]
    public async Task DisconnectAsync_ReleasesResources()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12345,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_client.IsConnected);

        await _client.DisconnectAsync();
        // After disconnect, the object should be clean
    }

    [Fact]
    public async Task Dispose_AfterConnect_ReleasesAll()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12345,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _client.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_WithoutConnect_DoesNotThrow()
    {
        _client = new RoomClient();
        _client.Dispose(); // Should not throw
    }

    [Fact]
    public async Task DoubleDispose_DoesNotThrow()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12345,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _client.Dispose();
        _client.Dispose(); // Second dispose should not throw
    }
}
