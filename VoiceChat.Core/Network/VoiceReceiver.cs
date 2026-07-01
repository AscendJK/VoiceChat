using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

/// <summary>
/// 语音接收器 - 支持丢包检测、乱序重排、抖动统计
/// </summary>
public class VoiceReceiver : IVoiceReceiver, IDisposable
{
    private UdpClient _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;
    private int _lastKnownPort;

    private readonly ConcurrentDictionary<string, bool> _mutedUsers = new();

    // 每个用户的包跟踪
    private readonly ConcurrentDictionary<string, UserPacketTracker> _trackers = new();

    public event Action<VoicePacket>? OnVoiceReceived;
    /// <summary>
    /// 丢包通知（userId, lostCount）— 接收方可调用 PLC 生成补偿帧
    /// </summary>
    public event Action<string, int>? OnPacketsLost;
    public VoiceReceiveStats Stats { get; } = new();
    public bool IsReceiving { get; private set; }

    public VoiceReceiver(int port)
    {
        _udpClient = new UdpClient(port);
        _lastKnownPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;
        // 增大UDP接收缓冲区，减少丢包
        _udpClient.Client.ReceiveBufferSize = 256 * 1024; // 256KB
        // 允许端口重用，避免Stop后立即Restart绑定失败
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    }

    public int LocalPort
    {
        get
        {
            try { _lastKnownPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port; }
            catch { }
            return _lastKnownPort;
        }
    }

    public void Start()
    {
        if (IsReceiving) return;
        IsReceiving = true;

        // 如果 UdpClient 已被关闭则重建（使用之前保存的端口号）
        int targetPort = 0;
        try
        {
            var _ = _udpClient.Client.LocalEndPoint;
        }
        catch
        {
            // UdpClient 已释放，需要重建
            targetPort = _lastKnownPort;
        }

        if (targetPort > 0)
        {
            try { _udpClient.Dispose(); } catch { }
            _udpClient = new UdpClient(targetPort);
            _udpClient.Client.ReceiveBufferSize = 256 * 1024;
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        }

        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsReceiving) return;
        IsReceiving = false;

        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        // 先关闭 UdpClient 强制中断 ReceiveAsync（立即解除阻塞）
        try { _udpClient.Close(); } catch { }
        // 等待接收任务退出（最多 500ms，因为 UdpClient 已关闭，应该很快）
        try { _receiveTask?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
    }

    public void MuteUser(string userId) => _mutedUsers[userId] = true;
    public void UnmuteUser(string userId) => _mutedUsers.TryRemove(userId, out _);
    public bool IsUserMuted(string userId) => _mutedUsers.ContainsKey(userId);
    public void RemoveUserTracker(string userId) { _trackers.TryRemove(userId, out _); _mutedUsers.TryRemove(userId, out _); }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsReceiving)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                if (result.Buffer.Length == 0) continue;

                Interlocked.Increment(ref Stats._receivedPackets);
                Interlocked.Add(ref Stats._receivedBytes, result.Buffer.Length);

                var packets = VoicePacket.DeserializeMultiple(result.Buffer);
                if (packets.Count == 0) continue;

                foreach (var packet in packets)
                {
                    if (IsUserMuted(packet.UserId))
                    {
                        Interlocked.Increment(ref Stats._droppedPackets);
                        continue;
                    }

                    // 丢包检测和乱序重排
                    var tracker = _trackers.GetOrAdd(packet.UserId, _ => new UserPacketTracker());
                    tracker.ProcessPacket(packet, out var readyPacket, out int lostCount);

                    if (lostCount > 0)
                    {
                        Interlocked.Add(ref Stats._lostPackets, lostCount);
                        OnPacketsLost?.Invoke(packet.UserId, lostCount);
                    }

                    if (readyPacket != null)
                    {
                        OnVoiceReceived?.Invoke(readyPacket);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            catch
            {
                // 未知错误：短暂延迟防止 100% CPU 空转
                try { await Task.Delay(50, cancellationToken); } catch { break; }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IsReceiving = false;
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        // 先关闭 UdpClient 强制中断 ReceiveAsync
        try { _udpClient?.Close(); } catch { }
        // 等待接收任务退出（最多 500ms），防止回调访问已释放对象
        try { _receiveTask?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        _udpClient?.Dispose();
        _cts?.Dispose();
        _cts = null;
    }
}

/// <summary>
/// 用户包跟踪器 - 丢包检测、乱序重排
/// </summary>
internal class UserPacketTracker
{
    private uint _expectedSeq;
    private bool _isFirstPacket = true;
    private readonly SortedList<uint, VoicePacket> _reorderBuffer = new();
    private const int MaxReorderBufferSize = 10;
    private const uint SeqWrapAround = uint.MaxValue / 2;

    public void ProcessPacket(VoicePacket packet, out VoicePacket? readyPacket, out int lostCount)
    {
        readyPacket = null;
        lostCount = 0;

        if (_isFirstPacket)
        {
            _expectedSeq = packet.SequenceNumber + 1;
            _isFirstPacket = false;
            readyPacket = packet;
            return;
        }

        int diff = (int)(packet.SequenceNumber - _expectedSeq);

        // 正常顺序包
        if (diff == 0)
        {
            _expectedSeq = packet.SequenceNumber + 1;
            readyPacket = packet;
        }
        // 未来包（在预期之后 — 中间序列号视为丢失）
        else if (diff > 0 && diff < (int)SeqWrapAround / 2)
        {
            lostCount = diff;
            _expectedSeq = packet.SequenceNumber + 1;

            // 存入缓冲区，同时立即输出（LAN 场景下间隙>0 基本等于丢包）
            if (!_reorderBuffer.ContainsKey(packet.SequenceNumber))
            {
                _reorderBuffer[packet.SequenceNumber] = packet;
            }
            readyPacket = packet;
        }
        // 乱序包（在预期之前 — 延迟到达的包）
        else if (diff < 0 && diff > -(int)SeqWrapAround / 2)
        {
            // 存入重排序缓冲区
            _reorderBuffer[packet.SequenceNumber] = packet;

            // 如果这个包正好填补了预期之前的空隙，立即输出并推进预期
            if (packet.SequenceNumber == _expectedSeq - 1)
            {
                _reorderBuffer.Remove(packet.SequenceNumber);
                _expectedSeq = packet.SequenceNumber + 1;
                readyPacket = packet;
            }
        }
        // 太旧的包，丢弃
        else
        {
            return;
        }

        // 检查重排序缓冲区是否有后续连续可输出的包
        while (_reorderBuffer.Count > 0 && _reorderBuffer.TryGetValue(_expectedSeq, out var nextPacket))
        {
            _reorderBuffer.Remove(_expectedSeq);
            _expectedSeq = nextPacket.SequenceNumber + 1;
            readyPacket = nextPacket;
        }

        // 如果缓冲区太大，强制输出最早的包
        if (_reorderBuffer.Count > MaxReorderBufferSize)
        {
            var oldest = _reorderBuffer.Values[0];
            _reorderBuffer.RemoveAt(0);
            _expectedSeq = oldest.SequenceNumber + 1;
            readyPacket = oldest;
        }
    }
}

/// <summary>
/// 语音接收统计
/// </summary>
public class VoiceReceiveStats
{
    internal long _receivedPackets;
    internal long _receivedBytes;
    internal long _droppedPackets;
    internal long _lostPackets;

    public long ReceivedPackets => Interlocked.Read(ref _receivedPackets);
    public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);
    public long DroppedPackets => Interlocked.Read(ref _droppedPackets);
    public long LostPackets => Interlocked.Read(ref _lostPackets);

    public double PacketLossRate =>
        _receivedPackets + _lostPackets > 0
            ? (double)Interlocked.Read(ref _lostPackets) / (Interlocked.Read(ref _receivedPackets) + Interlocked.Read(ref _lostPackets)) * 100
            : 0;

    public void Reset()
    {
        Interlocked.Exchange(ref _receivedPackets, 0);
        Interlocked.Exchange(ref _receivedBytes, 0);
        Interlocked.Exchange(ref _droppedPackets, 0);
        Interlocked.Exchange(ref _lostPackets, 0);
    }
}
