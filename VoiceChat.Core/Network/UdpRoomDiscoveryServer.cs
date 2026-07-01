using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VoiceChat.Core.Models;

namespace VoiceChat.Core.Network;

/// <summary>
/// 房间发现服务端（房主使用）- 定期广播房间信息
/// </summary>
public class UdpRoomDiscoveryServer : IDisposable
{
    private UdpClient? _udpClient;
    private Timer? _broadcastTimer;
    private readonly RoomInfo _roomInfo;
    private readonly int _port;
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public event Action<string>? OnLog;

    public UdpRoomDiscoveryServer(RoomInfo roomInfo, int port = 9999)
    {
        _roomInfo = roomInfo;
        _port = port;
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;

        try
        {
            _udpClient = new UdpClient();
            _udpClient.EnableBroadcast = true;

            // 立即广播一次
            BroadcastRoomInfo();

            // 每1000ms广播一次（降低CPU，VOIP场景中1s延迟无感知）
            _broadcastTimer = new Timer(_ => BroadcastRoomInfo(), null, 1000, 1000);

            OnLog?.Invoke($"广播心跳已启动，端口 {_port}");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"启动失败: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _broadcastTimer?.Dispose();
        _broadcastTimer = null;

        _udpClient?.Close();
        _udpClient = null;
    }

    public void UpdateRoomInfo(int memberCount)
    {
        _roomInfo.MemberCount = memberCount;
    }

    private void BroadcastRoomInfo()
    {
        if (_udpClient == null || !IsRunning) return;

        try
        {
            var message = new
            {
                Type = "RoomAnnounce",
                RoomId = _roomInfo.Id,
                RoomName = _roomInfo.Name,
                HostName = _roomInfo.HostName,
                HostAddress = _roomInfo.HostAddress,
                SignalingPort = _roomInfo.SignalingPort,
                VoicePort = _roomInfo.VoicePort,
                MemberCount = _roomInfo.MemberCount,
                MaxMembers = _roomInfo.MaxMembers,
                HasPassword = _roomInfo.HasPassword,
                Quality = _roomInfo.Quality.Bitrate // 包含音质码率，让客户端显示正确
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            // 发送到所有网卡的广播地址，确保所有子网的电脑都能发现房间
            var ips = GetLocalIPv4Addresses();
            foreach (var ip in ips)
            {
                var subnetBroadcast = GetSubnetBroadcast(ip);
                if (subnetBroadcast != null)
                {
                    var ep = new IPEndPoint(subnetBroadcast, _port);
                    _udpClient.Send(bytes, bytes.Length, ep);
                }
            }

            // 同时也发送到有限广播地址（兼容旧方式）
            var broadcastEP = new IPEndPoint(IPAddress.Broadcast, _port);
            _udpClient.Send(bytes, bytes.Length, broadcastEP);
        }
        catch
        {
            // 忽略广播错误
        }
    }

    private static List<IPAddress> GetLocalIPv4Addresses()
    {
        var result = new List<IPAddress>();
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    result.Add(ip);
            }
        }
        catch { }
        return result;
    }

    private static IPAddress? GetSubnetBroadcast(IPAddress address)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.Equals(address) && ua.IPv4Mask != null)
                    {
                        var addr = address.GetAddressBytes();
                        var mask = ua.IPv4Mask.GetAddressBytes();
                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcast[i] = (byte)(addr[i] | ~mask[i]);
                        return new IPAddress(broadcast);
                    }
                }
            }
        }
        catch { }
        // 找不到子网掩码时，使用C类广播地址（255.255.255.0 → x.x.x.255）
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            bytes[3] = 255;
            return new IPAddress(bytes);
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
