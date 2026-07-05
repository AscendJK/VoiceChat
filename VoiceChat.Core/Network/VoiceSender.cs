using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

/// <summary>
/// 语音发送器
/// </summary>
public class VoiceSender : IVoiceSender, IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<string, IPEndPoint> _endpoints = new();
    private uint _sequenceNumber;

    // 缓存 UserId 字节和预分配缓冲区，避免每帧分配
    private byte[] _userIdBytes = Array.Empty<byte>();
    private string _lastUserId = string.Empty; // 用于检测 UserId 变化
    private byte[] _sendBuffer = Array.Empty<byte>();

    public string UserId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public VoiceSendStats Stats { get; } = new();

    public VoiceSender(int localPort = 0)
    {
        _udpClient = new UdpClient(localPort);
    }

    public int LocalPort => ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

    public void AddEndpoint(string userId, IPEndPoint endpoint)
    {
        _endpoints[userId] = endpoint;
    }

    public void RemoveEndpoint(string userId)
    {
        _endpoints.TryRemove(userId, out _);
        _endpointFailures.TryRemove(userId, out _);
    }

    public void ClearEndpoints()
    {
        _endpoints.Clear();
        _endpointFailures.Clear();
    }

    public void SendVoice(byte[] audioData, int length)
    {
        if (!IsEnabled || _endpoints.IsEmpty) return;

        EnsureUserIdBytes();
        int totalSize = 19 + _userIdBytes.Length + length;
        EnsureBufferSize(totalSize);

        int offset = WritePacketHeader(_sendBuffer, 0, Interlocked.Increment(ref _sequenceNumber), audioData, length);
        SendRaw(_sendBuffer, totalSize);
    }

    /// <summary>
    /// 发送合并的音频包（2帧合并），零分配
    /// </summary>
    public void SendCombinedVoice(byte[] audioData1, int length1, byte[] audioData2, int length2)
    {
        if (!IsEnabled || _endpoints.IsEmpty) return;

        EnsureUserIdBytes();
        int totalSize = (19 + _userIdBytes.Length + length1) + (19 + _userIdBytes.Length + length2);
        EnsureBufferSize(totalSize);

        int offset = 0;
        offset = WritePacketHeader(_sendBuffer, offset, Interlocked.Increment(ref _sequenceNumber), audioData1, length1);
        offset = WritePacketHeader(_sendBuffer, offset, Interlocked.Increment(ref _sequenceNumber), audioData2, length2);

        SendRaw(_sendBuffer, totalSize);
    }

    /// <summary>
    /// 确保 UserId 字节缓存有效
    /// </summary>
    private void EnsureUserIdBytes()
    {
        if (_lastUserId != UserId)
        {
            _userIdBytes = System.Text.Encoding.UTF8.GetBytes(UserId);
            _lastUserId = UserId;
        }
    }

    /// <summary>
    /// 确保发送缓冲区足够大
    /// </summary>
    private void EnsureBufferSize(int requiredSize)
    {
        if (_sendBuffer.Length < requiredSize)
        {
            _sendBuffer = new byte[requiredSize * 2]; // 双倍分配避免频繁扩容
        }
    }

    /// <summary>
    /// 直接写入包头到缓冲区，返回下一个写入位置
    /// </summary>
    private int WritePacketHeader(byte[] buffer, int offset, uint seqNum, byte[] audioData, int audioLen)
    {
        // PacketType (1 byte)
        buffer[offset++] = 0x01;

        // UserId (2-byte length prefix little endian + data)
        buffer[offset++] = (byte)(_userIdBytes.Length & 0xFF);
        buffer[offset++] = (byte)((_userIdBytes.Length >> 8) & 0xFF);
        Buffer.BlockCopy(_userIdBytes, 0, buffer, offset, _userIdBytes.Length);
        offset += _userIdBytes.Length;

        // Timestamp (8 bytes)
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Buffer.BlockCopy(BitConverter.GetBytes(timestamp), 0, buffer, offset, 8);
        offset += 8;

        // SequenceNumber (4 bytes)
        Buffer.BlockCopy(BitConverter.GetBytes(seqNum), 0, buffer, offset, 4);
        offset += 4;

        // AudioDataLength (4 bytes)
        Buffer.BlockCopy(BitConverter.GetBytes(audioLen), 0, buffer, offset, 4);
        offset += 4;

        // AudioData
        if (audioLen > 0)
        {
            Buffer.BlockCopy(audioData, 0, buffer, offset, audioLen);
            offset += audioLen;
        }

        return offset;
    }

    // 端点连续失败次数跟踪（超过阈值后自动移除）
    private readonly ConcurrentDictionary<string, int> _endpointFailures = new();
    private const int MaxEndpointFailuresBeforeRemove = 50;
    private readonly List<string> _removeBuffer = new();

    private void SendRaw(byte[] data, int length)
    {
        if (_endpoints.IsEmpty) return;

        _removeBuffer.Clear();

        foreach (var kvp in _endpoints)
        {
            try
            {
                _udpClient.Send(data, length, kvp.Value);
                Interlocked.Increment(ref Stats._sentPackets);
                Interlocked.Add(ref Stats._sentBytes, length);
                // 发送成功，重置失败计数
                _endpointFailures[kvp.Key] = 0;
            }
            catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset)
            {
                // ICMP 端口不可达：立即隔离该端点，防止影响其他端点
                Interlocked.Increment(ref Stats._failedPackets);
                _removeBuffer.Add(kvp.Key);
            }
            catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.HostUnreachable || ex.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable)
            {
                // 网络不可达：立即隔离
                Interlocked.Increment(ref Stats._failedPackets);
                _removeBuffer.Add(kvp.Key);
            }
            catch
            {
                // 其他错误（如临时缓冲区满）：累计失败次数
                Interlocked.Increment(ref Stats._failedPackets);
                int failures = IncrementFailures(kvp.Key);
                if (failures >= MaxEndpointFailuresBeforeRemove)
                {
                    _removeBuffer.Add(kvp.Key);
                }
            }
        }

        // 移除连续失败的端点
        foreach (var key in _removeBuffer)
        {
            _endpoints.TryRemove(key, out _);
            _endpointFailures.TryRemove(key, out _);
        }
    }

    private int IncrementFailures(string endpointKey)
    {
        return _endpointFailures.AddOrUpdate(endpointKey, 1, (key, oldValue) => oldValue + 1);
    }

    public void Dispose()
    {
        _udpClient?.Dispose();
    }
}

public class VoiceSendStats
{
    internal long _sentPackets;
    internal long _sentBytes;
    internal long _failedPackets;

    public long SentPackets => Interlocked.Read(ref _sentPackets);
    public long SentBytes => Interlocked.Read(ref _sentBytes);
    public long FailedPackets => Interlocked.Read(ref _failedPackets);

    public void Reset()
    {
        Interlocked.Exchange(ref _sentPackets, 0);
        Interlocked.Exchange(ref _sentBytes, 0);
        Interlocked.Exchange(ref _failedPackets, 0);
    }
}