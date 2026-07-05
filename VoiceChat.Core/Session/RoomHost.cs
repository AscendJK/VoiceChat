using NAudio.CoreAudioApi;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;

namespace VoiceChat.Core.Session;

/// <summary>
/// 房间管理器（房主）
/// </summary>
public class RoomHost : IRoomHost, IDisposable, IAsyncDisposable
{
    private UdpRoomDiscoveryServer? _discoveryServer;
    private SignalingServer? _server;
    private VoiceSender? _voiceSender;
    private VoiceReceiver? _voiceReceiver;
    private AudioCapture? _audioCapture;
    private AudioPlayer? _audioPlayer;
    private OpusCodec? _codec;

    private readonly ConcurrentDictionary<string, RoomMember> _members = new();
    private readonly ConcurrentDictionary<string, IPEndPoint> _voiceEndpoints = new();

    // 编码发送队列（解耦采集和网络IO），最大容量约200ms音频（10帧@20ms）
    private readonly ConcurrentQueue<short[]> _audioQueue = new();
    private readonly System.Threading.SemaphoreSlim _audioSignal = new(0);
    private const int MaxAudioQueueSize = 10;
    private CancellationTokenSource? _sendCts;
    private Task? _sendTask;
    // 缓存事件处理程序引用，确保能正确取消订阅
    private Action<float>? _inputVolumeHandler;

    /// <summary>
    /// 房间信息
    /// </summary>
    public RoomInfo RoomInfo { get; private set; } = new();

    /// <summary>
    /// 房主成员
    /// </summary>
    public RoomMember HostMember { get; private set; } = new();

    /// <summary>
    /// 当前成员列表（线程安全快照）
    /// </summary>
    public List<RoomMember> Members
    {
        get { lock (_members) { return _members.Values.ToList(); } }
    }

    /// <summary>
    /// 获取音频采集器（用于外部控制噪声门限等设置）
    /// </summary>
    public AudioCapture? GetAudioCapture() => _audioCapture;

    /// <summary>
    /// 获取发送统计
    /// </summary>
    public VoiceSendStats? GetSendStats() => _voiceSender?.Stats;

    /// <summary>
    /// 获取接收统计
    /// </summary>
    public VoiceReceiveStats? GetReceiveStats() => _voiceReceiver?.Stats;

    /// <summary>
    /// 最后收到语音包的时间戳（UTC 毫秒）
    /// </summary>
    public long LastReceiveTimestamp => Interlocked.Read(ref _lastReceiveTimestamp);
    private long _lastReceiveTimestamp;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; private set; }

    public event Action<RoomMember>? OnMemberJoined;
    public event Action<string>? OnMemberLeft;
    public event Action<string, bool>? OnMemberMuteChanged;
    public event Action<string>? OnUserSpeaking;
    public event Action<string>? OnError;
    /// <summary>
    /// 诊断/统计信息（非错误）
    /// </summary>
    public event Action<string>? OnStats;
    public event Action<float>? OnInputVolumeChanged;

    public async Task<bool> CreateAsync(string roomName, string hostName, int port = 0, string? password = null, VoiceQuality? quality = null)
    {
        // 幂等保护：防止重复创建
        if (IsRunning)
        {
            OnError?.Invoke("房间已存在，请先关闭当前房间");
            return false;
        }

        try
        {
            var hostAddress = GetLocalIPAddress();
            if (string.IsNullOrEmpty(hostAddress))
            {
                OnError?.Invoke("无法获取本机IP地址");
                return false;
            }

            _server = new SignalingServer();
            await _server.StartAsync(port);
            var signalingPort = _server.Port;

            _voiceSender = new VoiceSender();
            _voiceReceiver = new VoiceReceiver(0);
            var voicePort = _voiceReceiver.LocalPort;

            var q = quality ?? VoiceQuality.Standard;

            _audioCapture = new AudioCapture
            {
                SampleRate = q.SampleRate,
                Channels = q.Channels,
                FrameSizeMs = q.FrameSizeMs
            };
            _audioCapture.Initialize();
            _audioCapture.OnFrameReady += OnAudioFrameReady;
            _inputVolumeHandler = volume => OnInputVolumeChanged?.Invoke(volume);
            _audioCapture.InputVolumeChanged += _inputVolumeHandler;

            _audioPlayer = new AudioPlayer
            {
                SampleRate = q.SampleRate,
                Channels = q.Channels
            };
            _audioPlayer.Initialize();

            _codec = new OpusCodec(q.SampleRate, q.Channels, q.Bitrate);

            var hostEP = new IPEndPoint(IPAddress.Parse(hostAddress), voicePort);
            HostMember = new RoomMember
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = hostName,
                IsHost = true,
                VoiceEndPoint = hostEP,
                VoiceAddress = hostEP.Address.ToString(),
                VoicePort = hostEP.Port
            };

            RoomInfo = new RoomInfo
            {
                Name = roomName,
                HostName = hostName,
                HostAddress = hostAddress,
                SignalingPort = signalingPort,
                VoicePort = voicePort,
                HasPassword = !string.IsNullOrEmpty(password),
                MemberCount = 1,
                Quality = q
            };

            _members[HostMember.Id] = HostMember;
            _voiceSender.UserId = HostMember.Id;
            _server.SetHostMember(HostMember);
            _server.SetPassword(password);

            _discoveryServer = new UdpRoomDiscoveryServer(RoomInfo);
            _discoveryServer.OnLog += msg => OnStats?.Invoke($"[发现] {msg}");

            _server.OnMemberJoin += HandleMemberJoin;
            _server.OnMemberLeave += HandleMemberLeave;
            _server.OnMemberMuteChanged += HandleMemberMuteChanged;
            _server.OnVoiceEndpointRegistered += HandleVoiceEndpointRegistered;

            _voiceReceiver.OnVoiceReceived += HandleVoiceReceived;
            _voiceReceiver.OnPacketsLost += HandlePacketsLost;
            _voiceReceiver.Start();

            _audioCapture.Start();
            _audioPlayer.Start();

            _sendCts = new CancellationTokenSource();
            _sendTask = Task.Run(() => SendLoopAsync(_sendCts.Token));

            _discoveryServer.Start();

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"创建房间失败: {ex.Message}");
            // 清理已部分创建的资源
            CleanupPartialResources();
            return false;
        }
    }

    /// <summary>
    /// 清理 CreateAsync 异常时已部分创建的资源
    /// </summary>
    private void CleanupPartialResources()
    {
        // 先取消事件订阅，防止回调访问已释放对象
        UnsubscribeEvents();

        _sendCts?.Dispose();
        _sendCts = null;

        _audioCapture?.Dispose();
        _audioCapture = null;

        _audioPlayer?.Dispose();
        _audioPlayer = null;

        _codec?.Dispose();
        _codec = null;

        _voiceSender?.Dispose();
        _voiceSender = null;

        _voiceReceiver?.Dispose();
        _voiceReceiver = null;

        try { _server?.Stop(); } catch { }
        _server?.Dispose();
        _server = null;

        _discoveryServer?.Dispose();
        _discoveryServer = null;

        IsRunning = false;
    }

    public async Task CloseAsync()
    {
        if (!IsRunning) return;

        // 先广播房间解散消息（在停止发现服务之前）
        try
        {
            if (_server != null)
                await _server.BroadcastRoomDissolvedAsync();
        }
        catch { }

        // 等待TCP缓冲区排空，确保RoomDissolved到达客户端
        await Task.Delay(200);

        try { _discoveryServer?.Stop(); } catch { }
        try { _audioCapture?.Stop(); } catch { }
        try { _audioPlayer?.Stop(); } catch { }
        try { _voiceReceiver?.Stop(); } catch { }

        try
        {
            _sendCts?.Cancel();
            if (_sendTask != null)
                await _sendTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { }

        try
        {
            if (_server != null)
                await _server.StopAsync();
        }
        catch { }

        IsRunning = false;
    }

    public async Task KickMemberAsync(string memberId)
    {
        if (_server == null) return;

        // 先立即停止音频处理（不等服务器响应）
        _voiceSender?.RemoveEndpoint(memberId);
        _voiceReceiver?.MuteUser(memberId);
        _audioPlayer?.RemoveUser(memberId);

        // 再通知服务器踢出
        await _server.KickMemberAsync(memberId);

        // 从本地集合移除
        if (_members.TryRemove(memberId, out _))
        {
            _voiceEndpoints.TryRemove(memberId, out _);
            UpdateMemberCount();
            OnMemberLeft?.Invoke(memberId);
        }
    }

    public void MuteMember(string memberId, bool mute)
    {
        if (_members.TryGetValue(memberId, out var member))
        {
            member.IsMutedByMe = mute;

            if (mute)
            {
                // 立即停止接收和播放该用户音频
                _voiceSender?.RemoveEndpoint(memberId);
                _voiceReceiver?.MuteUser(memberId);
                _audioPlayer?.RemoveUser(memberId);
            }
            else
            {
                _voiceReceiver?.UnmuteUser(memberId);
                // 恢复发送音频到该用户
                if (_voiceEndpoints.TryGetValue(memberId, out var endpoint))
                {
                    _voiceSender?.AddEndpoint(memberId, endpoint);
                }
            }
        }
    }

    public void MuteSelf(bool mute)
    {
        if (_audioCapture == null) return;

        if (mute)
        {
            _audioCapture.Stop();
            HostMember.IsMuted = true;
        }
        else
        {
            _audioCapture.Start();
            HostMember.IsMuted = false;
        }

        // 广播静音状态给房间其他成员
        _ = BroadcastMuteStateAsync(mute);
    }

    private async Task BroadcastMuteStateAsync(bool mute)
    {
        try
        {
            if (_server == null || !_server.IsRunning) return;
            await _server.BroadcastAsync(new SignalingMessage
            {
                Type = mute ? SignalingType.MuteSelf : SignalingType.UnmuteSelf,
                SenderId = HostMember.Id
            });
        }
        catch { }
    }

    /// <summary>
    /// 切换音频设备
    /// </summary>
    public void SwitchAudioDevice(MMDevice device)
    {
        _audioCapture?.SwitchDevice(device);
    }

    /// <summary>
    /// 切换播放设备
    /// </summary>
    public void SwitchPlaybackDevice(MMDevice device)
    {
        _audioPlayer?.SwitchPlaybackDevice(device);
    }

    /// <summary>
    /// 设置用户音量
    /// </summary>
    public void SetUserVolume(string userId, float volume)
    {
        _audioPlayer?.SetUserVolume(userId, volume);
        if (_members.TryGetValue(userId, out var member))
        {
            member.Volume = volume;
        }
    }

    private void HandleMemberJoin(RoomMember member, IPEndPoint endPoint)
    {
        _members[member.Id] = member;

        if (member.VoiceEndPoint != null)
        {
            _voiceEndpoints[member.Id] = member.VoiceEndPoint;
            _voiceSender?.AddEndpoint(member.Id, member.VoiceEndPoint);
        }

        UpdateMemberCount();
        OnMemberJoined?.Invoke(member);
    }

    private void HandleMemberLeave(string memberId)
    {
        if (_members.TryRemove(memberId, out var removedMember))
        {
            _voiceEndpoints.TryRemove(memberId, out _);
            _voiceSender?.RemoveEndpoint(memberId);
            _voiceReceiver?.RemoveUserTracker(memberId);
            _audioPlayer?.RemoveUser(memberId);

            UpdateMemberCount();
            OnMemberLeft?.Invoke(memberId);
        }
        else
        {
        }
    }

    private void HandleMemberMuteChanged(string memberId, bool isMuted)
    {
        if (_members.TryGetValue(memberId, out var member))
        {
            member.IsMuted = isMuted;
            OnMemberMuteChanged?.Invoke(memberId, isMuted);
        }
    }

    private void HandleVoiceEndpointRegistered(string memberId, IPEndPoint endPoint)
    {
        _voiceEndpoints[memberId] = endPoint;
        _voiceSender?.AddEndpoint(memberId, endPoint);
    }

    private DateTime _lastStatsLog = DateTime.MinValue;

    private void OnAudioFrameReady(short[] frame, int count)
    {
        // VAD：静音不发送
        if (AudioPreprocessor.IsSilent(frame, count))
            return;

        var buffer = ArrayPool<short>.Shared.Rent(count);
        Buffer.BlockCopy(frame, 0, buffer, 0, count * 2);

        // 队列满时丢弃最旧帧，防止无限积压
        while (_audioQueue.Count >= MaxAudioQueueSize)
        {
            if (_audioQueue.TryDequeue(out var dropped))
                ArrayPool<short>.Shared.Return(dropped);
        }
        _audioQueue.Enqueue(buffer);
        try { _audioSignal.Release(); } catch { }
    }

    // 预分配编码缓冲区，避免每帧 new byte[]
    private byte[]? _encodeBuf1;
    private byte[]? _encodeBuf2;

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 检查对象是否已被释放（防止 Dispose 后访问）
                if (_codec == null || _voiceSender == null) break;

                // 确保缓冲区已分配（只分配一次）
                _encodeBuf1 ??= new byte[_codec.MaxPacketSize];
                _encodeBuf2 ??= new byte[_codec.MaxPacketSize];

                // 尝试获取2帧进行合并发送
                if (_audioQueue.TryDequeue(out var frame1))
                {
                    try
                    {
                        int encodedLength1 = _codec.Encode(frame1, frame1.Length, _encodeBuf1);

                        if (encodedLength1 > 0)
                        {
                            // 尝试获取第二帧
                            if (_audioQueue.TryDequeue(out var frame2))
                            {
                                try
                                {
                                    int encodedLength2 = _codec.Encode(frame2, frame2.Length, _encodeBuf2);

                                    if (encodedLength2 > 0)
                                    {
                                        _voiceSender.SendCombinedVoice(_encodeBuf1, encodedLength1, _encodeBuf2, encodedLength2);
                                        Interlocked.Add(ref _totalSamplesSent, frame1.Length + frame2.Length);
                                        Interlocked.Add(ref _totalFramesSent, 2);
                                    }
                                    else
                                    {
                                        _voiceSender.SendVoice(_encodeBuf1, encodedLength1);
                                        Interlocked.Add(ref _totalSamplesSent, frame1.Length);
                                        Interlocked.Increment(ref _totalFramesSent);
                                    }
                                }
                                finally
                                {
                                    ArrayPool<short>.Shared.Return(frame2);
                                }
                            }
                            else
                            {
                                _voiceSender.SendVoice(_encodeBuf1, encodedLength1);
                                Interlocked.Add(ref _totalSamplesSent, frame1.Length);
                                Interlocked.Increment(ref _totalFramesSent);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<short>.Shared.Return(frame1);
                    }
                }
                else
                {
                    // 等待信号或超时（100ms），避免忙等待
                    // 使用 try-catch 防止 _audioSignal 已释放时抛出
                    try
                    {
                        await _audioSignal.WaitAsync(100, cancellationToken);
                    }
                    catch (ObjectDisposedException) { break; }
                }

                // 每5秒输出统计
                if (DateTime.UtcNow - _lastStatsLog > TimeSpan.FromSeconds(5))
                {
                    _lastStatsLog = DateTime.UtcNow;
                    long samplesPerSecond = Interlocked.Read(ref _totalSamplesSent) / 5;
                    long framesPerSecond = Interlocked.Read(ref _totalFramesSent) / 5;
                    OnStats?.Invoke($"[发送] 帧率:{framesPerSecond}/s 采样率:{samplesPerSecond}/s 队列:{_audioQueue.Count}");
                    Interlocked.Exchange(ref _totalSamplesSent, 0);
                    Interlocked.Exchange(ref _totalFramesSent, 0);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // _audioSignal 已释放，优雅退出
        }
    }

    private long _totalSamplesSent = 0;
    private long _totalFramesSent = 0;

    private long _hostReceiveCount = 0;
    private DateTime _hostLastStatsLog = DateTime.MinValue;

    private void HandleVoiceReceived(VoicePacket packet)
    {
        if (_codec == null || _audioPlayer == null) return;

        // 记录最后接收时间戳（用于延迟计算）
        Interlocked.Exchange(ref _lastReceiveTimestamp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        OnUserSpeaking?.Invoke(packet.UserId);
        Interlocked.Increment(ref _hostReceiveCount);

        var decoded = new short[_codec.FrameSize];
        var samplesDecoded = _codec.Decode(packet.AudioData, packet.AudioDataLength, decoded);

        if (samplesDecoded > 0)
        {
            _audioPlayer.AddAudioData(packet.UserId, decoded, samplesDecoded);
        }

        if (DateTime.UtcNow - _hostLastStatsLog > TimeSpan.FromSeconds(5))
        {
            _hostLastStatsLog = DateTime.UtcNow;
            long packetsPerSecond = Interlocked.Read(ref _hostReceiveCount) / 5;
            OnStats?.Invoke($"[接收] 包率:{packetsPerSecond}/s Seq:{packet.SequenceNumber} From:{packet.UserId}");
            Interlocked.Exchange(ref _hostReceiveCount, 0);
        }
    }

    private void UpdateMemberCount()
    {
        RoomInfo.MemberCount = _members.Count;
        _discoveryServer?.UpdateRoomInfo(_members.Count);
    }

    /// <summary>
    /// 丢包补偿 — 用 Opus PLC 生成预测音频填补缺口，避免静音
    /// </summary>
    private void HandlePacketsLost(string userId, int lostCount)
    {
        if (_codec == null || _audioPlayer == null) return;

        var decoded = new short[_codec.FrameSize];
        for (int i = 0; i < lostCount; i++)
        {
            var samplesDecoded = _codec.Decode(null!, 0, decoded, lostPacket: true);
            if (samplesDecoded > 0)
            {
                _audioPlayer.AddAudioData(userId, decoded, samplesDecoded);
            }
        }
    }

    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return string.Empty;
    }

    public void Dispose()
    {
        // 同步释放：按正确顺序停止所有组件，防止 use-after-free

        // 1. 取消发送任务并等待退出
        if (_sendCts != null && !_sendCts.IsCancellationRequested)
        {
            _sendCts.Cancel();
        }
        try
        {
            if (_sendTask != null)
                _sendTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        _sendCts?.Dispose();
        _sendCts = null;

        // 2. 停止发现服务（不再广播）
        try { _discoveryServer?.Stop(); } catch { }

        // 3. 停止音频采集和播放
        try { _audioCapture?.Stop(); } catch { }
        try { _audioPlayer?.Stop(); } catch { }
        try { _voiceReceiver?.Stop(); } catch { }

        // 4. 取消所有事件订阅（在 Dispose 组件之前，防止回调访问已释放对象）
        UnsubscribeEvents();

        // 5. 释放所有资源（按创建顺序的逆序）
        _discoveryServer?.Dispose();
        _server?.Dispose();
        _voiceSender?.Dispose();
        _voiceReceiver?.Dispose();
        _audioPlayer?.Dispose();
        _codec?.Dispose();

        // 6. 释放音频采集（停止 WASAPI 回调）
        try { _audioCapture?.Dispose(); } catch { }

        // 7. 等待任何正在执行的 WASAPI 回调完成（防止回调访问已释放的 semaphore）
        Thread.Sleep(50);

        // 8. 最后释放 SemaphoreSlim（确保没有回调仍在等待/释放）
        //    使用 try-catch 防止已释放的 semaphore 被回调访问时抛出
        try { _audioSignal.Dispose(); } catch { }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 取消所有事件订阅（防止 Dispose 后仍有事件回调访问已释放对象）
    /// </summary>
    private void UnsubscribeEvents()
    {
        if (_audioCapture != null)
        {
            _audioCapture.OnFrameReady -= OnAudioFrameReady;
            if (_inputVolumeHandler != null)
                _audioCapture.InputVolumeChanged -= _inputVolumeHandler;
        }
        if (_server != null)
        {
            _server.OnMemberJoin -= HandleMemberJoin;
            _server.OnMemberLeave -= HandleMemberLeave;
            _server.OnMemberMuteChanged -= HandleMemberMuteChanged;
            _server.OnVoiceEndpointRegistered -= HandleVoiceEndpointRegistered;
        }
        if (_voiceReceiver != null)
        {
            _voiceReceiver.OnVoiceReceived -= HandleVoiceReceived;
            _voiceReceiver.OnPacketsLost -= HandlePacketsLost;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        UnsubscribeEvents();
        _sendCts?.Dispose();
        _discoveryServer?.Dispose();
        _server?.Dispose();
        _voiceSender?.Dispose();
        _voiceReceiver?.Dispose();
        _audioCapture?.Dispose();
        _audioPlayer?.Dispose();
        _codec?.Dispose();
        _audioSignal.Dispose();
        GC.SuppressFinalize(this);
    }
}
