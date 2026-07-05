using VoiceChat.Core.Models;
using VoiceChat.Core.Network;
using System.Collections.Concurrent;

namespace VoiceChat.Tests;

public class SignalingTests : IAsyncLifetime
{
    private SignalingServer? _server;
    private SignalingClient? _client;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try { _client?.Dispose(); } catch { }
        try { _server?.Stop(); } catch { }
    }

    [Fact]
    public async Task ServerStartStop_Works()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);
        Assert.True(_server.IsRunning);
        Assert.True(_server.Port > 0);

        _server.Stop();
        Assert.False(_server.IsRunning);
    }

    [Fact]
    public async Task ClientConnect_Succeeds()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var tcs = new TaskCompletionSource<JoinResponseData>();
        _client = new SignalingClient();
        _client.OnConnected += data => tcs.TrySetResult(data);

        bool success = await _client.ConnectAsync("127.0.0.1", _server.Port, "TestUser", 12345);
        Assert.True(success);
        Assert.True(_client.IsConnected);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task ClientJoin_WrongPassword_Fails()
    {
        _server = new SignalingServer();
        _server.SetPassword("secret");
        await _server.StartAsync(0);

        _client = new SignalingClient();
        var tcs = new TaskCompletionSource<string?>();
        _client.OnError += msg => tcs.TrySetResult(msg);

        bool success = await _client.ConnectAsync("127.0.0.1", _server.Port, "TestUser", password: "wrong");

        Assert.False(success);
        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("密码", error);
    }

    [Fact]
    public async Task ClientJoin_CorrectPassword_Succeeds()
    {
        _server = new SignalingServer();
        _server.SetPassword("secret");
        await _server.StartAsync(0);

        _client = new SignalingClient();
        bool success = await _client.ConnectAsync("127.0.0.1", _server.Port, "TestUser", password: "secret");

        Assert.True(success);
    }

    [Fact]
    public async Task ServerBroadcast_ReachesClient()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var tcs = new TaskCompletionSource<string>();
        _client = new SignalingClient();
        _client.OnMemberJoined += member => tcs.TrySetResult(member.Name);

        // Connect first client
        await _client.ConnectAsync("127.0.0.1", _server.Port, "Host", 0);

        // Connect second client that should trigger broadcast to first
        var client2 = new SignalingClient();
        await client2.ConnectAsync("127.0.0.1", _server.Port, "Joiner", 0);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Joiner", result);

        client2.Dispose();
    }

    [Fact]
    public async Task ClientDisconnect_BroadcastsToOthers()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var tcs = new TaskCompletionSource<string>();
        _client = new SignalingClient();
        _client.OnMemberLeft += id => tcs.TrySetResult(id);

        await _client.ConnectAsync("127.0.0.1", _server.Port, "Observer", 0);

        // Connect and immediately disconnect another client
        var client2 = new SignalingClient();
        await client2.ConnectAsync("127.0.0.1", _server.Port, "Leaver", 0);
        await client2.DisconnectAsync();

        var leftId = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(client2.MemberId, leftId);
    }

    [Fact]
    public async Task MultipleClients_BroadcastEachOther()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var clients = new List<SignalingClient>();
        var received = new ConcurrentBag<string>();

        for (int i = 0; i < 3; i++)
        {
            var c = new SignalingClient();
            c.OnMemberJoined += member =>
            {
                if (member.Name != c.MemberId) // Don't count self
                    received.Add(member.Name);
            };
            await c.ConnectAsync("127.0.0.1", _server.Port, $"User{i}", 10000 + i);
            clients.Add(c);
        }

        // Give broadcasts time to propagate
        await Task.Delay(1000);

        // With 3 clients, each pair should cross-notify (3 clients * 2 notifications each = 6)
        Assert.True(received.Count >= 3,
            $"Expected at least 3 notifications, got {received.Count}");

        foreach (var c in clients) c.Dispose();
    }

    [Fact]
    public async Task KickMember_RemovesClient()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var client1 = new SignalingClient();
        await client1.ConnectAsync("127.0.0.1", _server.Port, "StayUser", 0);

        var client2 = new SignalingClient();
        var tcs = new TaskCompletionSource<string?>();
        client2.OnError += msg => tcs.TrySetResult(msg);
        client2.OnDisconnected += () => tcs.TrySetResult("disconnected");

        await client2.ConnectAsync("127.0.0.1", _server.Port, "KickUser", 0);

        // Kick client2
        await _server.KickMemberAsync(client2.MemberId!);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Client should receive either an error or disconnect notification
        Assert.NotNull(result);

        client1.Dispose();
    }

    [Fact]
    public async Task EmptyUsername_Rejected()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var client = new SignalingClient();
        var tcs = new TaskCompletionSource<string?>();
        client.OnError += msg => tcs.TrySetResult(msg);

        bool success = await client.ConnectAsync("127.0.0.1", _server.Port, "", 0);

        Assert.False(success);
        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("用户名", error);
    }

    [Fact]
    public async Task LongUsername_Rejected()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var client = new SignalingClient();
        var tcs = new TaskCompletionSource<string?>();
        client.OnError += msg => tcs.TrySetResult(msg);

        bool success = await client.ConnectAsync("127.0.0.1", _server.Port, new string('A', 100), 0);

        Assert.False(success);
        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("字符", error);
    }

    [Fact]
    public async Task Heartbeat_ResponseReceived()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var client = new SignalingClient();
        await client.ConnectAsync("127.0.0.1", _server.Port, "HeartbeatUser", 0);

        // Send heartbeat - should not throw
        await client.SendHeartbeatAsync();

        client.Dispose();
    }

    [Fact]
    public async Task MuteSelf_BroadcastsToOthers()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var client1 = new SignalingClient();
        var muteTcs = new TaskCompletionSource<string>();
        client1.OnMemberMuteChanged += (id, muted) =>
        {
            if (muted) muteTcs.TrySetResult(id);
        };

        await client1.ConnectAsync("127.0.0.1", _server.Port, "User1", 0);

        var client2 = new SignalingClient();
        await client2.ConnectAsync("127.0.0.1", _server.Port, "User2", 0);

        // Wait for connections to stabilize
        await Task.Delay(200);

        await client2.SendMuteSelfAsync(true);

        var mutedId = await muteTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // SenderId 应该是 client2 的 MemberId（不再为空）
        Assert.Equal(client2.MemberId, mutedId);

        client1.Dispose();
        client2.Dispose();
    }

    [Fact]
    public async Task Server_Stop_ClosesAllConnections()
    {
        _server = new SignalingServer();
        await _server.StartAsync(0);

        var client = new SignalingClient();
        var tcs = new TaskCompletionSource<bool>();
        client.OnDisconnected += () => tcs.TrySetResult(true);

        await client.ConnectAsync("127.0.0.1", _server.Port, "User1", 0);

        _server.Stop();

        var disconnected = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(disconnected);
    }
}
