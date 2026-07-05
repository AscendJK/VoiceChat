using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

/// <summary>
/// 房间扫描器（成员使用）- 监听广播心跳
/// </summary>
public class UdpBroadcasterScanner : IDisposable
{
    private readonly int _port;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _expireTask;

    public ConcurrentDictionary<string, RoomInfo> DiscoveredRooms { get; } = new();

    public event Action<RoomInfo>? OnRoomDiscovered;
    public event Action<RoomInfo>? OnRoomUpdated;
    public event Action<string>? OnRoomExpired;
    public event Action<string>? OnError;

    public UdpBroadcasterScanner(int port = 9999)
    {
        _port = port;
    }

    /// <summary>是否成功启动并监听端口</summary>
    public bool IsRunning => _listenTask != null;

    public void Start()
    {
        if (_listenTask != null) return;

        try
        {
            _udpClient = new UdpClient(_port);
            _udpClient.EnableBroadcast = true;
            _cts = new CancellationTokenSource();

            _listenTask = Task.Run(() => ListenLoop(_cts.Token));
            _expireTask = Task.Run(() => ExpireLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"扫描器启动失败（端口 {_port} 被占用）: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        // 先关闭 UdpClient 强制中断 ReceiveAsync
        try { _udpClient?.Close(); } catch { }
        // 等待两个任务退出（最多 1 秒）
        try
        {
            var tasks = new List<Task>();
            if (_listenTask != null) tasks.Add(_listenTask);
            if (_expireTask != null) tasks.Add(_expireTask);
            if (tasks.Count > 0)
                Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(1));
        }
        catch { }
        _cts?.Dispose();
        _listenTask = null;
        _expireTask = null;
    }

    private async Task ListenLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync();

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(result.Buffer);
                    if (doc.RootElement.TryGetProperty("Type", out var typeProp) &&
                        typeProp.GetString() == "RoomAnnounce")
                    {
                        ProcessResponse(doc.RootElement, result.RemoteEndPoint);
                    }
                }
                catch { }
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    /// <summary>
    /// 发送主动探测包，触发范围内的房主立即回应
    /// </summary>
    public async Task ProbeAsync()
    {
        try
        {
            using var probeClient = new UdpClient();
            probeClient.EnableBroadcast = true;
            var probe = Encoding.UTF8.GetBytes("{\"Type\":\"RoomProbe\"}");
            await probeClient.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, _port));
        }
        catch { /* 探测失败不影响被动监听 */ }
    }

    /// <summary>
    /// 定期清除超过2秒未更新的房间
    /// </summary>
    private async Task ExpireLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);

                var now = DateTime.UtcNow;
                var expiredIds = DiscoveredRooms
                    .Where(kvp => (now - kvp.Value.LastBroadcastTime.ToUniversalTime()) > TimeSpan.FromSeconds(2))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var id in expiredIds)
                {
                    DiscoveredRooms.TryRemove(id, out _);
                    OnRoomExpired?.Invoke(id);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void ProcessResponse(JsonElement root, IPEndPoint remoteEndPoint)
    {
        try
        {
            // 解析音质码率（旧版房主协议可能不包含此字段，默认 Standard）
            int qualityBitrate = 64000;
            if (root.TryGetProperty("Quality", out var qualityProp))
            {
                qualityBitrate = qualityProp.GetInt32();
            }

            var roomInfo = new RoomInfo
            {
                Id = root.GetProperty("RoomId").GetString() ?? "",
                Name = root.GetProperty("RoomName").GetString() ?? "",
                HostName = root.GetProperty("HostName").GetString() ?? "",
                HostAddress = remoteEndPoint.Address.ToString(),
                SignalingPort = root.GetProperty("SignalingPort").GetInt32(),
                VoicePort = root.GetProperty("VoicePort").GetInt32(),
                MemberCount = root.GetProperty("MemberCount").GetInt32(),
                MaxMembers = root.GetProperty("MaxMembers").GetInt32(),
                HasPassword = root.GetProperty("HasPassword").GetBoolean(),
                Quality = BitrateToQuality(qualityBitrate),
                LastBroadcastTime = DateTime.UtcNow
            };

            var isNew = !DiscoveredRooms.ContainsKey(roomInfo.Id);
            DiscoveredRooms[roomInfo.Id] = roomInfo;

            if (isNew)
                OnRoomDiscovered?.Invoke(roomInfo);
            else
                OnRoomUpdated?.Invoke(roomInfo);
        }
        catch { }
    }

    /// <summary>
    /// 将码率值映射到最接近的音质配置
    /// </summary>
    private static VoiceQuality BitrateToQuality(int bitrate)
    {
        return bitrate switch
        {
            <= 64000 => VoiceQuality.Standard,
            <= 96000 => VoiceQuality.HighDefinition,
            _ => VoiceQuality.UltraHigh
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
