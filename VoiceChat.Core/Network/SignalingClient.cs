using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

/// <summary>
/// 信令客户端（成员使用）
/// </summary>
public class SignalingClient : ISignalingClient, IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly object _membersLock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disconnectNotified;

    public string ServerAddress { get; private set; } = string.Empty;
    public int ServerPort { get; private set; }
    public string? MemberId { get; private set; }
    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <summary>
    /// 成员列表（访问时需加锁）
    /// </summary>
    private readonly List<RoomMember> _members = new();
    public List<RoomMember> Members
    {
        get { lock (_membersLock) { return new List<RoomMember>(_members); } }
    }

    /// <summary>
    /// 房主成员信息
    /// </summary>
    public RoomMember? HostMember { get; private set; }

    public event Action<JoinResponseData>? OnConnected;
    public event Action<RoomMember>? OnMemberJoined;
    public event Action<string>? OnMemberLeft;
    public event Action<string, bool>? OnMemberMuteChanged;
    public event Action? OnDisconnected;
    public event Action? OnRoomDissolved;
    public event Action<string>? OnError;

    public async Task<bool> ConnectAsync(string address, int port, string userName, int voicePort = 0, CancellationToken cancellationToken = default, string? password = null)
    {
        try
        {
            ServerAddress = address;
            ServerPort = port;

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(address, port, cancellationToken);
            _stream = _tcpClient.GetStream();
            _tcpClient.NoDelay = true;
            _cts = new CancellationTokenSource();
            _disconnectNotified = false;

            // 发送加入请求（包含密码）
            var joinRequest = new SignalingMessage
            {
                Type = SignalingType.JoinRequest,
                Data = JsonSerializer.Serialize(new JoinRequestData
                {
                    UserName = userName,
                    VoicePort = voicePort,
                    Password = password
                })
            };

            await SendMessageAsync(joinRequest);

            // 等待响应（传递 CancellationToken 以支持超时取消）
            var response = await ReceiveMessageAsync(cancellationToken);
            if (response == null)
            {
                OnError?.Invoke("服务器无响应");
                return false;
            }

            if (response.Type != SignalingType.JoinResponse)
            {
                OnError?.Invoke($"响应类型不匹配: {response.Type}");
                return false;
            }

            var responseData = JsonSerializer.Deserialize<JoinResponseData>(response.Data);
            if (responseData == null)
            {
                OnError?.Invoke("服务器响应数据无效");
                return false;
            }

            if (!responseData.Success)
            {
                OnError?.Invoke(responseData.ErrorMessage ?? "加入失败");
                return false;
            }

            MemberId = responseData.MemberId;
            HostMember = responseData.HostMember;
            if (responseData.Members != null)
            {
                lock (_membersLock)
                {
                    _members.Clear();
                    _members.AddRange(responseData.Members);
                }
            }

            // 启动接收循环
            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));

            OnConnected?.Invoke(responseData);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"连接失败: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected)
            {
                await SendMessageAsync(new SignalingMessage
                {
                    Type = SignalingType.LeaveRequest
                });
            }
        }
        catch
        {
        }
        finally
        {
            _cts?.Cancel();

            // 先关闭 TCP 连接，强制中断 ReceiveLoop 中的 ReadAsync（防止静默断开时永久阻塞）
            try { _stream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }

            // 等待接收任务退出（最多 1 秒）
            if (_receiveTask != null)
            {
                try { await _receiveTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
            }

            // 释放资源
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }
            _stream = null;
            _tcpClient = null;

            NotifyDisconnected();
        }
    }

    public async Task SendMuteSelfAsync(bool isMuted)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = isMuted ? SignalingType.MuteSelf : SignalingType.UnmuteSelf
        });
    }

    public async Task SendHeartbeatAsync()
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = SignalingType.Heartbeat
        });
    }

    private void NotifyDisconnected()
    {
        if (!_disconnectNotified)
        {
            _disconnectNotified = true;
            OnDisconnected?.Invoke();
        }
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                var message = await ReceiveMessageAsync(cancellationToken);
                if (message == null)
                {
                    break;
                }
                HandleMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            NotifyDisconnected();
        }
    }

    private void HandleMessage(SignalingMessage message)
    {
        switch (message.Type)
        {
            case SignalingType.MemberJoined:
                var newMember = JsonSerializer.Deserialize<RoomMember>(message.Data);
                if (newMember != null)
                {
                    lock (_membersLock)
                    {
                        // 去重：如果已存在则先删除再添加（更新数据），不存在则直接添加
                        _members.RemoveAll(m => m.Id == newMember.Id);
                        _members.Add(newMember);
                    }
                    OnMemberJoined?.Invoke(newMember);
                }
                break;

            case SignalingType.MemberLeft:
                var leftMemberId = message.Data;
                if (!string.IsNullOrEmpty(leftMemberId))
                {
                    int removedCount;
                    lock (_membersLock)
                    {
                        removedCount = _members.RemoveAll(m => m.Id == leftMemberId);
                    }
                    OnMemberLeft?.Invoke(leftMemberId);
                }
                break;

            case SignalingType.MuteSelf:
                UpdateMemberMute(message.SenderId, true);
                OnMemberMuteChanged?.Invoke(message.SenderId, true);
                break;

            case SignalingType.UnmuteSelf:
                UpdateMemberMute(message.SenderId, false);
                OnMemberMuteChanged?.Invoke(message.SenderId, false);
                break;

            case SignalingType.HeartbeatAck:
                break;

            case SignalingType.RoomDissolved:
                OnRoomDissolved?.Invoke();
                break;
        }
    }

    private void UpdateMemberMute(string memberId, bool isMuted)
    {
        lock (_membersLock)
        {
            var member = _members.FirstOrDefault(m => m.Id == memberId);
            if (member != null) member.IsMuted = isMuted;
        }
    }

    private async Task SendMessageAsync(SignalingMessage message)
    {
        var stream = _stream;
        if (stream == null || !IsConnected) return;

        var data = message.Serialize();

        // 使用写锁防止并发写入导致 TCP 帧交织
        await _writeLock.WaitAsync();
        try
        {
            await stream.WriteAsync(data);
            await stream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<SignalingMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        var stream = _stream;
        if (stream == null)
        {
            return null;
        }

        try
        {
            // 读取消息长度
            var lengthBuffer = new byte[4];
            var totalRead = 0;
            while (totalRead < 4)
            {
                var bytesRead = await stream.ReadAsync(lengthBuffer, totalRead, 4 - totalRead, cancellationToken);
                if (bytesRead == 0)
                {
                    return null;
                }
                totalRead += bytesRead;
            }

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > 1024 * 1024)
            {
                OnError?.Invoke($"无效消息长度: {length}");
                return null;
            }

            // 读取消息内容
            var buffer = new byte[length];
            totalRead = 0;
            while (totalRead < length)
            {
                var bytesRead = await stream.ReadAsync(buffer, totalRead, length - totalRead, cancellationToken);
                if (bytesRead == 0)
                {
                    return null;
                }
                totalRead += bytesRead;
            }

            var result = SignalingMessage.Deserialize(buffer);
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        // 先关闭连接强制中断 ReceiveLoop 中的 ReadAsync
        try { _stream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        // 等待接收任务退出（最多 500ms）
        try { _receiveTask?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        _cts?.Dispose();
        _cts = null;
        // 释放资源
        _writeLock.Dispose();
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }
}
