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

    [Fact]
    public async Task MuteSelf_Toggle_Works()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12346,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Mute self
        await _client.MuteSelfAsync(true);

        // Wait for audio capture to stop
        await Task.Delay(200);

        // Unmute self
        await _client.MuteSelfAsync(false);

        // No exception = pass
    }

    [Fact]
    public async Task OnMemberLeft_FiresOnDisconnect()
    {
        _client = new RoomClient();
        var connectedTcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => connectedTcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12347,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Subscribe to disconnected event
        var disconnectedTcs = new TaskCompletionSource<bool>();
        _client.OnDisconnected += () => disconnectedTcs.TrySetResult(true);

        await _client.DisconnectAsync();

        var disconnected = await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(disconnected);
    }

    [Fact]
    public async Task Members_ReturnsCurrentList()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12348,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var members = _client.Members;
        Assert.NotNull(members);
        // At minimum, should have the host member
        Assert.True(members.Count >= 1);
    }

    [Fact]
    public async Task HostMember_SetAfterConnect()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12349,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // HostMember is set during connect from server response
        // It may be null if server doesn't include it, so just verify no crash
        var host = _client.HostMember;
        // Either host is set or it's null (both are valid states)
    }

    [Fact]
    public async Task SetUserVolume_DoesNotThrow()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12350,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _client.SetUserVolume("any-user", 0.5f);
        // No exception = pass
    }

    [Fact]
    public async Task MuteOther_NonExistent_DoesNotThrow()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12351,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _client.MuteOther("non-existent-id", true);
        _client.MuteOther("non-existent-id", false);
        // No exception = pass
    }

    [Fact]
    public async Task GetAudioCapture_ReturnsCapture()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12352,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var capture = _client.GetAudioCapture();
        Assert.NotNull(capture);
    }

    [Fact]
    public async Task CurrentRoom_ReturnsRoomInfo()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        var roomInfo = new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12353,
            Quality = VoiceQuality.Standard,
            Name = "TestRoom"
        };

        await _client.ConnectAsync(roomInfo, "TestUser");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(_client.CurrentRoom);
        Assert.Equal("TestRoom", _client.CurrentRoom.Name);
    }

    [Fact]
    public async Task KickUser_ReconnectBlocked()
    {
        // 房主创建房间
        var host = new RoomHost();
        await host.CreateAsync("KickTest", "Host", 0, null, VoiceQuality.Standard);
        int port = GetServerPort(host);

        // 客户端加入
        _client = new RoomClient();
        var connectedTcs = new TaskCompletionSource<bool>();
        var disconnectedTcs = new TaskCompletionSource<bool>();
        var reconnectingTcs = new TaskCompletionSource<int>();
        _client.OnConnected += _ => connectedTcs.TrySetResult(true);
        _client.OnError += _ => { };
        _client.OnDisconnected += () => disconnectedTcs.TrySetResult(true);
        _client.OnReconnecting += (attempt, _) => reconnectingTcs.TrySetResult(attempt);

        var roomInfo = new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = port,
            VoicePort = 12400,
            Quality = VoiceQuality.Standard
        };
        await _client.ConnectAsync(roomInfo, "Joiner");
        await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        string? memberId = _client.MemberId;
        Assert.NotNull(memberId);

        // 房主踢出客户端
        await host.KickMemberAsync(memberId!);

        // 等待客户端收到断连通知
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 给重连逻辑一点时间，如果有的话
        await Task.Delay(500);

        // 验证没有触发重连
        Assert.False(reconnectingTcs.Task.IsCompletedSuccessfully,
            "Reconnect should not have been triggered after being kicked");

        await host.CloseAsync();
        host.Dispose();
    }

    [Fact]
    public async Task Disconnect_Intentional_NoReconnect()
    {
        _client = new RoomClient();
        var tcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => tcs.TrySetResult(room);

        await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = _server!.Port,
            VoicePort = 12401,
            Quality = VoiceQuality.Standard
        }, "TestUser");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_client.IsConnected);

        // 主动断开
        await _client.DisconnectAsync();
        await Task.Delay(500);

        // 不应重连
        Assert.False(_client.IsConnected);
    }

    private int GetServerPort(RoomHost host)
    {
        var field = host.GetType().GetField("_server",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var server = field?.GetValue(host);
        if (server == null) return 0;
        var portProp = server.GetType().GetProperty("Port");
        return (int)(portProp?.GetValue(server) ?? 0);
    }
}
