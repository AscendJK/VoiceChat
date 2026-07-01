using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

/// <summary>
/// 信令服务器（房主使用）
/// </summary>
public class SignalingServer : ISignalingServer, IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>
    /// 已连接的客户端
    /// </summary>
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();
    private const int HeartbeatTimeoutSeconds = 15;
    private Task? _sweepTask;
    private RoomMember? _hostMember;
    private readonly ConcurrentDictionary<string, Task> _clientTasks = new();

    /// <summary>
    /// 服务器端口
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 成员加入事件
    /// </summary>
    public event Action<RoomMember, IPEndPoint>? OnMemberJoin;

    /// <summary>
    /// 成员离开事件
    /// </summary>
    public event Action<string>? OnMemberLeave;

    /// <summary>
    /// 成员静音状态变更事件
    /// </summary>
    public event Action<string, bool>? OnMemberMuteChanged;

    /// <summary>
    /// 语音端点注册事件
    /// </summary>
    public event Action<string, IPEndPoint>? OnVoiceEndpointRegistered;

    /// <summary>
    /// 房间密码哈希（null 表示无密码）
    /// </summary>
    private string? _passwordHash;

    /// <summary>
    /// 设置房间密码
    /// </summary>
    public void SetPassword(string? password)
    {
        _passwordHash = string.IsNullOrEmpty(password) ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(password)));
    }

    /// <summary>
    /// 设置房主成员信息（用于发送给新成员）
    /// </summary>
    public void SetHostMember(RoomMember hostMember)
    {
        _hostMember = hostMember;
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync(int port = 0)
    {
        if (IsRunning) return;

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoop(_cts.Token));
        _sweepTask = Task.Run(() => HeartbeatSweepLoop(_cts.Token));
        IsRunning = true;

        await Task.CompletedTask;
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();

        // 关闭listener以解除AcceptTcpClientAsync的阻塞
        try { _listener?.Stop(); } catch { }

        // 先关闭所有客户端 TCP 连接，强制中断 ReadAsync（使 HandleClientLoop 退出）
        foreach (var client in _clients.Values)
        {
            try { client.Client.Close(); } catch { }
        }

        // 等待AcceptTask完成（最多2秒），确保循环退出
        try
        {
            _acceptTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        // 等待所有客户端处理任务完成（客户端已关闭，应该很快退出）
        try { Task.WaitAll(_clientTasks.Values.ToArray(), TimeSpan.FromSeconds(2)); } catch { }

        // 等待清扫任务完成
        try { _sweepTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }

        // 释放所有客户端资源
        foreach (var client in _clients.Values)
        {
            try { client.Dispose(); } catch { }
        }
        _clients.Clear();
        _clientTasks.Clear();
        _lastHeartbeat.Clear();

        // 释放资源
        _listener = null;
        _acceptTask = null;
        _cts?.Dispose();

        IsRunning = false;
    }

    /// <summary>
    /// 异步停止（兼容接口）
    /// </summary>
    public Task StopAsync()
    {
        Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 广播房间解散消息并关闭所有连接
    /// </summary>
    public async Task BroadcastRoomDissolvedAsync()
    {
        if (!IsRunning) return;

        await BroadcastAsync(new SignalingMessage
        {
            Type = SignalingType.RoomDissolved,
            Data = "房间已解散"
        });
    }

    // 广播合并队列：将 50ms 内的多个事件合并为一次广播，减少 TCP 写入次数
    private readonly struct PendingMessage
    {
        public readonly SignalingType Type;
        public readonly string Data;
        public readonly string? ExcludeId;

        public PendingMessage(SignalingType type, string data, string? excludeId)
        {
            Type = type;
            Data = data;
            ExcludeId = excludeId;
        }
    }
    private readonly ConcurrentQueue<PendingMessage> _broadcastQueue = new();
    private Task? _broadcastProcessorTask;
    private readonly object _broadcastLock = new();

    /// <summary>
    /// 向所有客户端广播消息（加入合并队列，50ms 内多个事件合并为一次发送）
    /// </summary>
    public Task BroadcastAsync(SignalingMessage message, string? excludeId = null)
    {
        _broadcastQueue.Enqueue(new PendingMessage(message.Type, message.Data, excludeId));

        // 启动后台合并处理器（如果未运行）
        lock (_broadcastLock)
        {
            if (_broadcastProcessorTask == null || _broadcastProcessorTask.IsCompleted)
            {
                _broadcastProcessorTask = Task.Run(ProcessBroadcastQueueAsync);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 广播合并处理器：等待 50ms 收集事件，然后一次性广播所有消息
    /// </summary>
    private async Task ProcessBroadcastQueueAsync()
    {
        // 等待 50ms 收集批量事件（VOIP 场景下 50ms 延迟无感知）
        await Task.Delay(50);

        var messages = new List<PendingMessage>();
        while (_broadcastQueue.TryDequeue(out var msg))
        {
            messages.Add(msg);
        }

        if (messages.Count == 0) return;

        // 获取当前客户端快照（避免在发送过程中集合被修改）
        var targetClients = _clients.ToList();

        // 向每个客户端发送所有待处理消息（带超时保护）
        foreach (var client in targetClients)
        {
            using var clientCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                foreach (var msg in messages)
                {
                    if (msg.ExcludeId == client.Key) continue;

                    var singleMessage = new SignalingMessage
                    {
                        Type = msg.Type,
                        SenderId = "",
                        Data = msg.Data
                    };

                    await SendToClientAsync(client.Value, singleMessage)
                        .WaitAsync(clientCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 单客户端超时，跳过该客户端的剩余消息
            }
        }
    }

    /// <summary>
    /// 向指定客户端发送消息
    /// </summary>
    public async Task SendToClientAsync(string connectionId, SignalingMessage message)
    {
        if (_clients.TryGetValue(connectionId, out var client))
        {
            await SendToClientAsync(client, message);
        }
    }

    /// <summary>
    /// 踢出成员
    /// </summary>
    public async Task KickMemberAsync(string connectionId)
    {
        if (_clients.TryRemove(connectionId, out var client))
        {
            client.LeaveNotified = true;

            try
            {
                await SendToClientAsync(client, new SignalingMessage
                {
                    Type = SignalingType.Error,
                    Data = "你已被房主踢出房间"
                });
            }
            catch { }

            client.Dispose();

            OnMemberLeave?.Invoke(connectionId);

            await BroadcastAsync(new SignalingMessage
            {
                Type = SignalingType.MemberLeft,
                Data = connectionId
            });
        }
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var listener = _listener;
                if (listener == null) break;

                var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);
                var connectionId = Guid.NewGuid().ToString("N");
                var connection = new ClientConnection(connectionId, tcpClient);

                _clients[connectionId] = connection;

                var task = Task.Run(() => HandleClientLoop(connection, cancellationToken));
                _clientTasks[connectionId] = task;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // 服务器已停止
                break;
            }
            catch
            {
                // 忽略接受错误
            }
        }
    }

    private async Task HandleClientLoop(ClientConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && connection.IsConnected)
            {
                var message = await ReceiveMessageAsync(connection);
                if (message == null)
                {
                    // 收到 null 表示连接关闭或数据损坏，主动关闭连接防止僵尸
                    try { connection.Client.Close(); } catch { }
                    break;
                }

                message.SenderId = connection.Id;
                await HandleMessageAsync(connection, message);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch
        {
            // 连接异常
        }
        finally
        {
            // 移除客户端连接和对应的任务
            if (_clients.TryRemove(connection.Id, out _))
            {
                connection.Dispose();
            }
            _clientTasks.TryRemove(connection.Id, out _);
            _lastHeartbeat.TryRemove(connection.Id, out _);

            // 仅当未被 HandleLeaveRequest 或 KickMemberAsync 通知时才通知
            if (!connection.LeaveNotified)
            {
                connection.LeaveNotified = true;
                OnMemberLeave?.Invoke(connection.Id);

                // 广播成员离开消息（防止断网时其他客户端收不到通知）
                _ = BroadcastAsync(new SignalingMessage
                {
                    Type = SignalingType.MemberLeft,
                    Data = connection.Id
                });
            }
        }
    }

    private async Task HandleMessageAsync(ClientConnection connection, SignalingMessage message)
    {
        switch (message.Type)
        {
            case SignalingType.JoinRequest:
                await HandleJoinRequest(connection, message);
                break;

            case SignalingType.LeaveRequest:
                await HandleLeaveRequest(connection);
                break;

            case SignalingType.MuteSelf:
                await HandleMuteSelf(connection, true);
                break;

            case SignalingType.UnmuteSelf:
                await HandleMuteSelf(connection, false);
                break;

            case SignalingType.Heartbeat:
                _lastHeartbeat[connection.Id] = DateTime.UtcNow;
                await SendToClientAsync(connection, new SignalingMessage
                {
                    Type = SignalingType.HeartbeatAck,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                break;
        }
    }

    private async Task HandleJoinRequest(ClientConnection connection, SignalingMessage message)
    {
        try
        {
            var requestData = JsonSerializer.Deserialize<JoinRequestData>(message.Data);
            if (requestData == null)
            {
                await SendJoinResponse(connection, false, null, "无效的请求数据");
                return;
            }

            // 输入验证：用户名不能为空，长度限制32字符，禁止控制字符
            var userName = (requestData.UserName ?? "").Trim();
            if (string.IsNullOrEmpty(userName))
            {
                await SendJoinResponse(connection, false, null, "用户名不能为空");
                return;
            }
            if (userName.Length > 32)
            {
                await SendJoinResponse(connection, false, null, "用户名不能超过32个字符");
                return;
            }
            foreach (char c in userName)
            {
                if (char.IsControl(c))
                {
                    await SendJoinResponse(connection, false, null, "用户名包含无效字符");
                    return;
                }
            }
            requestData.UserName = userName;

            // 密码验证
            if (_passwordHash != null)
            {
                var requestPwd = requestData.Password ?? "";
                var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(requestPwd)));
                if (!string.Equals(_passwordHash, requestHash, StringComparison.OrdinalIgnoreCase))
                {
                    await SendJoinResponse(connection, false, null, "密码错误");
                    return;
                }
            }

            // 注意：不基于 IP 清理旧连接，因为 NAT 场景下多个客户端可能共享同一公网 IP
            // 重复连接由客户端自己断开旧连接来处理，服务器不做主动判断

            var member = new RoomMember
            {
                Id = connection.Id,
                Name = requestData.UserName,
                ConnectionId = connection.Id,
                IsHost = false,
                JoinedAt = DateTime.UtcNow,
                LastActiveTime = DateTime.UtcNow
            };

            // 设置用户名到连接
            connection.UserName = requestData.UserName;

            if (requestData.VoicePort > 0)
            {
                var voiceEndpoint = new IPEndPoint(
                    ((IPEndPoint)connection.Client.Client.RemoteEndPoint!).Address,
                    requestData.VoicePort
                );
                member.VoiceEndPoint = voiceEndpoint;
                member.VoiceAddress = voiceEndpoint.Address.ToString();
                member.VoicePort = voiceEndpoint.Port;
                connection.VoiceAddress = member.VoiceAddress;
                connection.VoicePort = member.VoicePort;
                OnVoiceEndpointRegistered?.Invoke(connection.Id, voiceEndpoint);
            }

            // 通知其他成员
            var memberJson = JsonSerializer.Serialize(member);
            await BroadcastAsync(new SignalingMessage
            {
                Type = SignalingType.MemberJoined,
                Data = memberJson
            }, connection.Id);

            // 发送成功响应
            await SendJoinResponse(connection, true, member, null);

            // 初始化心跳时间戳（防止客户端不发心跳时永不清理）
            _lastHeartbeat[connection.Id] = DateTime.UtcNow;

            // 触发加入事件
            OnMemberJoin?.Invoke(member, ((IPEndPoint)connection.Client.Client.RemoteEndPoint!));
        }
        catch (Exception ex)
        {
            await SendJoinResponse(connection, false, null, ex.Message);
        }
    }

    private async Task SendJoinResponse(ClientConnection connection, bool success, RoomMember? member, string? error)
    {
        var response = new JoinResponseData
        {
            Success = success,
            MemberId = member?.Id,
            ErrorMessage = error,
            Members = success ? GetMembers() : null,
            HostMember = success ? _hostMember : null
        };

        await SendToClientAsync(connection, new SignalingMessage
        {
            Type = SignalingType.JoinResponse,
            Data = JsonSerializer.Serialize(response)
        });
    }

    private async Task HandleLeaveRequest(ClientConnection connection)
    {
        if (_clients.TryRemove(connection.Id, out _))
        {
            connection.LeaveNotified = true;
            OnMemberLeave?.Invoke(connection.Id);
            connection.Dispose();
            await BroadcastAsync(new SignalingMessage
            {
                Type = SignalingType.MemberLeft,
                Data = connection.Id
            });
        }
        }

    private async Task HandleMuteSelf(ClientConnection connection, bool isMuted)
    {
        OnMemberMuteChanged?.Invoke(connection.Id, isMuted);

        await BroadcastAsync(new SignalingMessage
        {
            Type = isMuted ? SignalingType.MuteSelf : SignalingType.UnmuteSelf,
            SenderId = connection.Id
        });
    }

    private List<RoomMember> GetMembers()
    {
        var members = new List<RoomMember>();

        // 包含房主
        if (_hostMember != null)
        {
            members.Add(new RoomMember
            {
                Id = _hostMember.Id,
                Name = _hostMember.Name,
                ConnectionId = _hostMember.Id,
                IsHost = true,
                VoiceEndPoint = _hostMember.VoiceEndPoint,
                VoiceAddress = _hostMember.VoiceAddress,
                VoicePort = _hostMember.VoicePort,
                JoinedAt = _hostMember.JoinedAt
            });
        }

        // 快照客户端集合（ConcurrentDictionary本身已是线程安全）
        var clientSnapshot = _clients.Values.ToArray();

        // 包含所有TCP客户端
        members.AddRange(clientSnapshot.Select(c => new RoomMember
        {
            Id = c.Id,
            Name = c.UserName,
            ConnectionId = c.Id,
            IsHost = false,
            VoiceAddress = c.VoiceAddress,
            VoicePort = c.VoicePort,
            VoiceEndPoint = (!string.IsNullOrEmpty(c.VoiceAddress) && c.VoicePort > 0)
                ? new IPEndPoint(IPAddress.Parse(c.VoiceAddress), c.VoicePort)
                : null,
            JoinedAt = c.ConnectedAt
        }));

        return members;
    }

    private async Task SendToClientAsync(ClientConnection connection, SignalingMessage message)
    {
        try
        {
            var data = message.Serialize();
            // 使用连接级锁防止并发写入导致 TCP 帧损坏
            await connection.WriteAsync(data, CancellationToken.None);
        }
        catch
        {
            // 发送失败，检查连接是否已断开
            // 仅在连接确实断开时才标记 LeaveNotified，防止临时 TCP 错误抑制真正的断开通知
            try
            {
                if (connection.Client.Client == null || !connection.Client.Connected)
                {
                    connection.LeaveNotified = true;
                }
            }
            catch
            {
                connection.LeaveNotified = true;
            }
        }
    }

    private async Task<SignalingMessage?> ReceiveMessageAsync(ClientConnection connection)
    {
        try
        {
            // 读取消息长度（循环读取确保读满4字节）
            var lengthBuffer = new byte[4];
            var totalRead = 0;
            while (totalRead < 4)
            {
                var bytesRead = await connection.Stream.ReadAsync(lengthBuffer, totalRead, 4 - totalRead);
                if (bytesRead == 0) return null; // 连接已关闭
                totalRead += bytesRead;
            }

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > 1024 * 1024) return null; // 最大1MB

            // 读取消息内容
            var buffer = new byte[length];
            totalRead = 0;
            while (totalRead < length)
            {
                var bytesRead = await connection.Stream.ReadAsync(buffer, totalRead, length - totalRead);
                if (bytesRead == 0) return null;
                totalRead += bytesRead;
            }

            return SignalingMessage.Deserialize(buffer);
        }
        catch
        {
            return null;
        }
    }

    private async Task HeartbeatSweepLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, cancellationToken); // 10s 间隔（匹配客户端心跳）
                var now = DateTime.UtcNow;
                var stale = _lastHeartbeat
                    .Where(kv => (now - kv.Value).TotalSeconds > HeartbeatTimeoutSeconds)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var id in stale)
                {
                    if (_clients.TryRemove(id, out var client))
                    {
                        _lastHeartbeat.TryRemove(id, out _);
                        client.LeaveNotified = true;
                        try { client.Dispose(); } catch { }
                        OnMemberLeave?.Invoke(id);
                        _ = BroadcastAsync(new SignalingMessage
                        {
                            Type = SignalingType.MemberLeft,
                            Data = id
                        });
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    public void Dispose()
    {
        Stop();
        if (_cts != null)
        {
            _cts.Dispose();
        }
    }
}

/// <summary>
/// 客户端连接
/// </summary>
internal class ClientConnection : IDisposable
{
    public string Id { get; }
    public string UserName { get; set; } = string.Empty;
    public TcpClient Client { get; }
    public NetworkStream Stream { get; }
    public DateTime ConnectedAt { get; }
    public bool IsConnected => Client.Connected;
    public string? VoiceAddress { get; set; }
    public int VoicePort { get; set; }
    public bool LeaveNotified { get; set; }

    // 防止并发写入 NetworkStream 导致 TCP 帧损坏
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ClientConnection(string id, TcpClient client)
    {
        Id = id;
        Client = client;
        Stream = client.GetStream();
        ConnectedAt = DateTime.UtcNow;
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await Stream.WriteAsync(data, cancellationToken);
            await Stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        Stream?.Dispose();
        Client?.Dispose();
    }
}
