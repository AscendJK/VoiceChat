using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Session;

namespace VoiceChat.Tests;

public class E2ETests : IAsyncLifetime
{
    private RoomHost? _host;
    private RoomClient? _client;

    public async Task InitializeAsync() { }

    public async Task DisposeAsync()
    {
        try
        {
            if (_client != null)
                await _client.DisconnectAsync();
        }
        catch { }
        try { _client?.Dispose(); } catch { }
        try { _host?.CloseAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { _host?.Dispose(); } catch { }
    }

    [Fact]
    public async Task FullLifecycle_CreateJoinLeave()
    {
        // 1. 创建房间
        _host = new RoomHost();
        var hostTcs = new TaskCompletionSource<RoomMember>();
        _host.OnMemberJoined += member => hostTcs.TrySetResult(member);

        bool created = await _host.CreateAsync("TestRoom", "Host", 0, null, VoiceQuality.Standard);
        Assert.True(created);
        Assert.True(_host.IsRunning);

        // 2. 客户端加入（使用房主的 RoomInfo）
        _client = new RoomClient();
        var clientTcs = new TaskCompletionSource<RoomInfo>();
        _client.OnConnected += room => clientTcs.TrySetResult(room);

        var hostInfo = _host.RoomInfo!;
        hostInfo.HostAddress = "127.0.0.1";

        bool joined = await _client.ConnectAsync(hostInfo, "Client1");
        Assert.True(joined);

        // 等待双方都知道对方
        var hostMember = await hostTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Client1", hostMember.Name);

        var clientRoom = await clientTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(clientRoom);

        // 3. 客户端离开
        await _client.DisconnectAsync();

        // 4. 房主解散房间
        await _host.CloseAsync();
        Assert.False(_host.IsRunning);
    }

    [Fact]
    public async Task HostSeesClientJoin()
    {
        _host = new RoomHost();
        var joinTcs = new TaskCompletionSource<RoomMember>();
        _host.OnMemberJoined += member => joinTcs.TrySetResult(member);

        await _host.CreateAsync("TestRoom", "Host", quality: VoiceQuality.Standard);

        var hostInfo = _host.RoomInfo!;
        hostInfo.HostAddress = "127.0.0.1";

        _client = new RoomClient();
        _client.OnConnected += _ => { };
        await _client.ConnectAsync(hostInfo, "Client1");

        var joined = await joinTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Client1", joined.Name);
    }

    [Fact]
    public async Task ClientSeesRoomDissolved()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "Host", quality: VoiceQuality.Standard);

        var hostInfo = _host.RoomInfo!;
        hostInfo.HostAddress = "127.0.0.1";

        _client = new RoomClient();
        var dissolvedTcs = new TaskCompletionSource<bool>();
        _client.OnConnected += _ => { };
        _client.OnRoomDissolved += () => dissolvedTcs.TrySetResult(true);

        await _client.ConnectAsync(hostInfo, "Client1");

        // Wait a moment to ensure connection is stable
        await Task.Delay(200);

        // 房主解散房间（通过 CloseAsync，会先广播 RoomDissolved）
        var closeTask = _host.CloseAsync();

        // Client should receive RoomDissolved within reasonable time
        var dissolved = await dissolvedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(dissolved, "Client should receive RoomDissolved notification");

        await closeTask;
    }

    [Fact]
    public async Task MultipleClients_CanSeeEachOther()
    {
        _host = new RoomHost();
        var joinCount = 0;
        _host.OnMemberJoined += _ => Interlocked.Increment(ref joinCount);
        await _host.CreateAsync("TestRoom", "Host", quality: VoiceQuality.Standard);
        int port = GetServerPort();

        // 加入两个客户端
        var clients = new List<RoomClient>();
        for (int i = 0; i < 2; i++)
        {
            var c = new RoomClient();
            var tcs = new TaskCompletionSource<RoomInfo>();
            c.OnConnected += room => tcs.TrySetResult(room);
            await c.ConnectAsync(new RoomInfo
            {
                HostAddress = "127.0.0.1",
                SignalingPort = port,
                VoicePort = 12345 + i,
                Quality = VoiceQuality.Standard
            }, $"Client{i}");
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clients.Add(c);
        }

        // 等待房主看到两个客户端
        await Task.Delay(500);
        Assert.Equal(2, joinCount);

        foreach (var c in clients)
        {
            await c.DisconnectAsync();
            c.Dispose();
        }
    }

    [Fact]
    public async Task PasswordedRoom_RejectsWrongPassword()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "Host", 0, "secret", VoiceQuality.Standard);
        int port = GetServerPort();

        _client = new RoomClient();
        var errorTcs = new TaskCompletionSource<string>();
        _client.OnError += msg => errorTcs.TrySetResult(msg);

        bool joined = await _client.ConnectAsync(new RoomInfo
        {
            HostAddress = "127.0.0.1",
            SignalingPort = port,
            VoicePort = 12345,
            Quality = VoiceQuality.Standard
        }, "Client1", "wrongpassword");

        Assert.False(joined);
        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("密码", error);
    }

    /// <summary>
    /// 获取服务器端口（通过反射，因为 SignalingServer.Port 在启动后才知道）
    /// </summary>
    private int GetServerPort()
    {
        // _host 创建时启动了 SignalingServer，但没有直接暴露端口
        // 这里用反射获取端口
        var field = _host!.GetType().GetField("_server",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var server = field?.GetValue(_host);
        if (server == null) return 0;
        var portProp = server.GetType().GetProperty("Port");
        return (int)(portProp?.GetValue(server) ?? 0);
    }
}
