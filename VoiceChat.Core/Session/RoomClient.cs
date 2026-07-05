using NAudio.CoreAudioApi;
using System.Buffers;
using System.Net;
using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;

namespace VoiceChat.Core.Session;

/// <summary>
/// 房间客户端（成员）
/// </summary>
public class RoomClient : IRoomClient, IDisposable, IAsyncDisposable
{
    private SignalingClient? _signalingClient;
    private VoiceSender? _voiceSender;
    private VoiceReceiver? _voiceReceiver;
    private AudioCapture? _audioCapture;
    private AudioPlayer? _audioPlayer;
    private OpusCodec? _codec;

    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;

    // 编码发送队列（解耦采集和网络IO）
    private const int MaxAudioQueueSize = 10;
    private readonly System.Collections.Concurrent.ConcurrentQueue<short[]> _audioQueue = new();
    private System.Threading.SemaphoreSlim _audioSignal = new(0);
    private CancellationTokenSource? _sendCts;
    private Task? _sendTask;

    public RoomInfo? CurrentRoom { get; private set; }
    public string? MemberId { get; private set; }
    public bool IsConnected => _signalingClient?.IsConnected ?? false;

    public List<RoomMember> Members => _signalingClient?.Members ?? new List<RoomMember>();
    public RoomMember? HostMember => _signalingClient?.HostMember;

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

    public event Action<RoomInfo>? OnConnected;
    public event Action? OnDisconnected;
    public event Action<RoomMember>? OnMemberJoined;
    public event Action<string>? OnMemberLeft;
    public event Action<string, bool>? OnMemberMuteChanged;
    public event Action? OnRoomDissolved;
    public event Action<string>? OnUserSpeaking;
    public event Action<string>? OnError;
    /// <summary>
    /// 诊断/统计信息（非错误）
    /// </summary>
    public event Action<string>? OnStats;
    public event Action<float>? OnInputVolumeChanged;
    /// <summary>重连状态变化（attemptCount, maxAttempts）</summary>
    public event Action<int, int>? OnReconnecting;
    /// <summary>重连成功</summary>
    public event Action? OnReconnected;

    // 重连参数
    private RoomInfo? _lastRoom;
    private string? _lastUserName;
    private string? _lastPassword;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private const int MaxReconnectAttempts = 5;
    private bool _intentionalDisconnect;

    public async Task<bool> ConnectAsync(RoomInfo room, string userName, string? password = null, CancellationToken cancellationToken = default)
    {
        // 幂等保护：防止重复连接
        if (IsConnected)
        {
            OnError?.Invoke("已连接到房间，请先断开当前连接");
            return false;
        }

        // 保存连接参数（用于重连）
        _lastRoom = room;
        _lastUserName = userName;
        _lastPassword = password;
        _intentionalDisconnect = false;

        try
        {
            CurrentRoom = room;

            var q = room.Quality ?? VoiceQuality.Standard;

            _audioCapture = new AudioCapture
            {
                SampleRate = q.SampleRate,
                Channels = q.Channels,
                FrameSizeMs = q.FrameSizeMs
            };
            _audioCapture.Initialize();
            _audioCapture.OnFrameReady += OnAudioFrameReady;
            _audioCapture.InputVolumeChanged += volume => OnInputVolumeChanged?.Invoke(volume);

            _audioPlayer = new AudioPlayer
            {
                SampleRate = q.SampleRate,
                Channels = q.Channels
            };
            _audioPlayer.Initialize();

            _codec = new OpusCodec(q.SampleRate, q.Channels, q.Bitrate);

            _voiceSender = new VoiceSender();
            _voiceReceiver = new VoiceReceiver(0);
            var voicePort = _voiceReceiver.LocalPort;

            _voiceReceiver.OnVoiceReceived += HandleVoiceReceived;
            _voiceReceiver.OnPacketsLost += HandlePacketsLost;
            _voiceReceiver.Start();

            _signalingClient = new SignalingClient();
            _signalingClient.OnConnected += HandleConnected;
            _signalingClient.OnDisconnected += HandleDisconnected;
            _signalingClient.OnMemberJoined += HandleMemberJoined;
            _signalingClient.OnMemberLeft += HandleMemberLeft;
            _signalingClient.OnMemberMuteChanged += HandleMemberMuteChanged;
            _signalingClient.OnRoomDissolved += HandleRoomDissolved;
            _signalingClient.OnError += HandleError;

            var success = await _signalingClient.ConnectAsync(
                room.HostAddress, room.SignalingPort, userName, voicePort, cancellationToken);

            if (success)
            {
                MemberId = _signalingClient.MemberId;
                _voiceSender.UserId = MemberId!;


                if (_signalingClient.HostMember?.VoiceEndPoint != null)
                {
                    _voiceSender.AddEndpoint(_signalingClient.HostMember.Id, _signalingClient.HostMember.VoiceEndPoint);
                }
                else
                {
                    var hostEndpoint = new IPEndPoint(IPAddress.Parse(room.HostAddress), room.VoicePort);
                    _voiceSender.AddEndpoint("host", hostEndpoint);
                }

                foreach (var member in _signalingClient.Members)
                {
                    if (member.VoiceEndPoint != null)
                    {
                        _voiceSender.AddEndpoint(member.Id, member.VoiceEndPoint);
                    }
                }

                _audioCapture.Start();
                _audioPlayer.Start();
                _sendCts = new CancellationTokenSource();
                _sendTask = Task.Run(() => SendAudioLoopAsync(_sendCts.Token));
                StartHeartbeat();

                OnConnected?.Invoke(room);
                return true;
            }

            // 连接失败，释放已创建的资源
            CleanupOnFailedConnect();
            return false;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"连接失败: {ex.Message}");
            CleanupOnFailedConnect();
            return false;
        }
    }

    /// <summary>
    /// 连接失败时释放已创建的资源
    /// </summary>
    private void CleanupOnFailedConnect()
    {
        _signalingClient?.Dispose();
        _signalingClient = null;

        _voiceReceiver?.Stop();
        _voiceReceiver?.Dispose();
        _voiceReceiver = null;

        _voiceSender?.Dispose();
        _voiceSender = null;

        _audioPlayer?.Dispose();
        _audioPlayer = null;

        _audioCapture?.Dispose();
        _audioCapture = null;

        _codec?.Dispose();
        _codec = null;
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;
        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();

        try
        {
            // 先取消心跳和发送队列（快速操作）
            _heartbeatCts?.Cancel();
            _sendCts?.Cancel();

            // 等待后台任务退出（最多 1 秒）
            try { if (_heartbeatTask != null) await _heartbeatTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
            try { if (_sendTask != null) await _sendTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }

            // 先发送 LeaveRequest 到服务器
            if (_signalingClient != null)
            {
                await _signalingClient.DisconnectAsync();
            }
        }
        finally
        {
            // 无论是否异常，都执行清理（防止状态残留）
            // 停止本地音频
            try { _audioCapture?.Stop(); } catch { }
            try { _audioPlayer?.Stop(); } catch { }
            try { _voiceReceiver?.Stop(); } catch { }

            // 释放 SignalingClient（包含 CancellationTokenSource）
            try { _signalingClient?.Dispose(); } catch { }

            // 重置状态，防止重连时残留旧数据
            CurrentRoom = null;
            MemberId = null;
            _signalingClient = null;
            _voiceSender = null;
            _voiceReceiver = null;
            _audioCapture = null;
            _audioPlayer = null;
            _codec = null;
            _heartbeatCts = null;
            _heartbeatTask = null;
            _sendCts = null;
            _sendTask = null;
            _audioSignal.Dispose();
            _audioSignal = new(0); // 重建，支持重连

            OnDisconnected?.Invoke();
        }
    }

    public async Task MuteSelfAsync(bool mute)
    {
        if (_signalingClient == null || !IsConnected) return;

        await _signalingClient.SendMuteSelfAsync(mute);

        if (_audioCapture != null)
        {
            if (mute)
                _audioCapture.Stop();
            else
                _audioCapture.Start();
        }
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
    }

    public void MuteOther(string memberId, bool mute)
    {
        var member = Members.FirstOrDefault(m => m.Id == memberId);
        if (member != null)
        {
            member.IsMutedByMe = mute;

            if (mute)
            {
                _voiceReceiver?.MuteUser(memberId);
                _audioPlayer?.RemoveUser(memberId);
            }
            else
            {
                _voiceReceiver?.UnmuteUser(memberId);
            }
        }
    }

    private void HandleConnected(JoinResponseData response) { }

    private void HandleDisconnected()
    {
        OnDisconnected?.Invoke();

        // 非主动断开时尝试重连
        if (!_intentionalDisconnect && _lastRoom != null && _lastUserName != null)
        {
            StartReconnect();
        }
    }

    private void StartReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        _reconnectTask = Task.Run(() => ReconnectLoop(_reconnectCts.Token));
    }

    private async Task ReconnectLoop(CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested || _intentionalDisconnect) return;

            OnReconnecting?.Invoke(attempt, MaxReconnectAttempts);

            // 指数退避：1s, 2s, 4s, 8s, 16s
            int delayMs = (int)Math.Pow(2, attempt - 1) * 1000;
            try { await Task.Delay(delayMs, cancellationToken); } catch { return; }

            if (cancellationToken.IsCancellationRequested || _intentionalDisconnect) return;

            try
            {
                // 重建所有组件
                var room = _lastRoom;
                if (room == null) return;
                var q = room.Quality ?? VoiceQuality.Standard;

                _audioCapture = new AudioCapture
                {
                    SampleRate = q.SampleRate,
                    Channels = q.Channels,
                    FrameSizeMs = q.FrameSizeMs
                };
                _audioCapture.Initialize();
                _audioCapture.OnFrameReady += OnAudioFrameReady;

                _audioPlayer = new AudioPlayer
                {
                    SampleRate = q.SampleRate,
                    Channels = q.Channels
                };
                _audioPlayer.Initialize();

                _codec = new OpusCodec(q.SampleRate, q.Channels, q.Bitrate);

                _voiceSender = new VoiceSender();
                _voiceReceiver = new VoiceReceiver(0);
                var voicePort = _voiceReceiver.LocalPort;

                _voiceReceiver.OnVoiceReceived += HandleVoiceReceived;
                _voiceReceiver.OnPacketsLost += HandlePacketsLost;
                _voiceReceiver.Start();

                _signalingClient = new SignalingClient();
                _signalingClient.OnConnected += HandleConnected;
                _signalingClient.OnDisconnected += HandleDisconnected;
                _signalingClient.OnMemberJoined += HandleMemberJoined;
                _signalingClient.OnMemberLeft += HandleMemberLeft;
                _signalingClient.OnMemberMuteChanged += HandleMemberMuteChanged;
                _signalingClient.OnRoomDissolved += HandleRoomDissolved;
                _signalingClient.OnError += HandleError;

                var success = await _signalingClient.ConnectAsync(
                    room.HostAddress, room.SignalingPort, _lastUserName ?? "", voicePort, cancellationToken);

                if (success)
                {
                    MemberId = _signalingClient?.MemberId;
                    if (MemberId != null && _voiceSender != null)
                        _voiceSender.UserId = MemberId;

                    if (_signalingClient?.HostMember?.VoiceEndPoint != null && _voiceSender != null)
                    {
                        _voiceSender.AddEndpoint(_signalingClient.HostMember.Id, _signalingClient.HostMember.VoiceEndPoint);
                    }

                    if (_voiceSender != null)
                    {
                        foreach (var member in _signalingClient?.Members ?? new List<RoomMember>())
                        {
                            if (member.VoiceEndPoint != null)
                                _voiceSender.AddEndpoint(member.Id, member.VoiceEndPoint);
                        }
                    }

                    _audioCapture.Start();
                    _audioPlayer.Start();
                    _sendCts = new CancellationTokenSource();
                    _sendTask = Task.Run(() => SendAudioLoopAsync(_sendCts.Token));
                    StartHeartbeat();

                    CurrentRoom = room;
                    OnReconnected?.Invoke();
                    return;
                }

                // 连接失败，清理资源
                CleanupAfterFailedReconnect();
            }
            catch
            {
                CleanupAfterFailedReconnect();
            }
        }

        // 重连失败
        OnError?.Invoke($"重连失败：已尝试 {MaxReconnectAttempts} 次");
    }

    private void CleanupAfterFailedReconnect()
    {
        try { _signalingClient?.Dispose(); } catch { }
        try { _voiceReceiver?.Stop(); } catch { }
        try { _voiceReceiver?.Dispose(); } catch { }
        try { _voiceSender?.Dispose(); } catch { }
        try { _audioPlayer?.Dispose(); } catch { }
        try { _audioCapture?.Dispose(); } catch { }
        try { _codec?.Dispose(); } catch { }

        _signalingClient = null;
        _voiceReceiver = null;
        _voiceSender = null;
        _audioPlayer = null;
        _audioCapture = null;
        _codec = null;
    }

    private void HandleRoomDissolved()
    {
        OnRoomDissolved?.Invoke();
    }

    private void HandleMemberJoined(RoomMember member)
    {
        if (!string.IsNullOrEmpty(member.VoiceAddress) && member.VoicePort > 0)
        {
            try
            {
                var ip = IPAddress.Parse(member.VoiceAddress);
                member.VoiceEndPoint = new IPEndPoint(ip, member.VoicePort);
            }
            catch { }
        }

        if (member.VoiceEndPoint != null)
        {
            _voiceSender?.AddEndpoint(member.Id, member.VoiceEndPoint);
        }

        OnMemberJoined?.Invoke(member);
    }

    private void HandleMemberLeft(string memberId)
    {
        _voiceSender?.RemoveEndpoint(memberId);
        _voiceReceiver?.RemoveUserTracker(memberId);
        _audioPlayer?.RemoveUser(memberId);
        OnMemberLeft?.Invoke(memberId);
    }

    private void HandleMemberMuteChanged(string memberId, bool isMuted)
    {
        OnMemberMuteChanged?.Invoke(memberId, isMuted);
    }

    private void HandleError(string error)
    {
        OnError?.Invoke(error);
    }

    private long _captureCount = 0;
    private long _sendCount = 0;
    private DateTime _lastSendStatsLog = DateTime.MinValue;

    private void OnAudioFrameReady(short[] frame, int count)
    {
        if (_voiceSender == null) return;

        Interlocked.Increment(ref _captureCount);

        // 复制frame并将编码/发送交给后台线程，避免阻塞音频采集
        var buffer = ArrayPool<short>.Shared.Rent(count);
        Buffer.BlockCopy(frame, 0, buffer, 0, count * 2);

        // VAD：静音不发送，节省带宽
        if (AudioPreprocessor.IsSilent(buffer, count))
        {
            ArrayPool<short>.Shared.Return(buffer);
            return;
        }

        // 队列满时丢弃最旧帧，防止内存无限增长
        if (_audioQueue.Count >= MaxAudioQueueSize)
        {
            if (_audioQueue.TryDequeue(out var dropped))
                ArrayPool<short>.Shared.Return(dropped);
        }
        _audioQueue.Enqueue(buffer);
        try { _audioSignal.Release(); } catch { }
    }

    private async Task SendAudioLoopAsync(CancellationToken cancellationToken)
    {
        // 预分配编码缓冲区，避免每帧分配（减少 GC 压力）
        byte[]? encoded = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_audioQueue.TryDequeue(out var buffer))
                {
                    try
                    {
                        if (_codec != null)
                        {
                            // 确保缓冲区已分配
                            encoded ??= new byte[_codec.MaxPacketSize];
                            int encodedLength = _codec.Encode(buffer, buffer.Length, encoded);

                            if (encodedLength > 0)
                            {
                                _voiceSender?.SendVoice(encoded, encodedLength);
                                Interlocked.Increment(ref _sendCount);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<short>.Shared.Return(buffer);
                    }
                }
                else
                {
                    // 等待信号或超时（100ms），避免忙等待
                    try
                    {
                        await _audioSignal.WaitAsync(100, cancellationToken);
                    }
                    catch (ObjectDisposedException) { break; }
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // 防止网络断开等持久错误导致 100% CPU 空转
                try { await Task.Delay(50, cancellationToken); } catch { }
            }
        }
    }

    private long _receiveCount = 0;
    private long _playCount = 0;
    private DateTime _lastReceiveStatsLog = DateTime.MinValue;

    private void HandleVoiceReceived(VoicePacket packet)
    {
        if (_codec == null || _audioPlayer == null) return;

        // 标记该用户正在说话（UI显示绿色指示器）
        OnUserSpeaking?.Invoke(packet.UserId);

        Interlocked.Increment(ref _receiveCount);

        var decoded = new short[_codec.FrameSize];
        var samplesDecoded = _codec.Decode(packet.AudioData, packet.AudioDataLength, decoded);

        if (samplesDecoded > 0)
        {
            _audioPlayer.AddAudioData(packet.UserId, decoded, samplesDecoded);
            Interlocked.Increment(ref _playCount);
        }

        if (DateTime.UtcNow - _lastReceiveStatsLog > TimeSpan.FromSeconds(5))
        {
            _lastReceiveStatsLog = DateTime.UtcNow;
            long packetsPerSecond = Interlocked.Read(ref _receiveCount) / 5;
            OnStats?.Invoke($"[音频] 接收:{packetsPerSecond}/s 播放:{_playCount} Seq:{packet.SequenceNumber}");
            Interlocked.Exchange(ref _receiveCount, 0);
            Interlocked.Exchange(ref _playCount, 0);
        }
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

    private void StartHeartbeat()
    {
        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;
        _heartbeatTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _signalingClient!.SendHeartbeatAsync();
                    await Task.Delay(10000, token); // 10s 间隔（降低 CPU，VOIP 场景无感知）
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // 发送心跳出错时等待后重试，但检查取消状态
                    try { await Task.Delay(10000, token); } catch { break; }
                }
            }
        });
    }

    /// <summary>
    /// 异步关闭房间客户端（内部使用，不发送 LeaveRequest）
    /// 适用于服务器已关闭或需要静默断开的场景
    /// </summary>
    public async Task CloseAsync()
    {
        if (!IsConnected) return;

        _heartbeatCts?.Cancel();
        _sendCts?.Cancel();

        // 等待后台任务退出
        try { if (_heartbeatTask != null) await _heartbeatTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        try { if (_sendTask != null) await _sendTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }

        // 停止本地音频
        _audioCapture?.Stop();
        _audioPlayer?.Stop();
        _voiceReceiver?.Stop();

        // 关闭信令连接（不发送 LeaveRequest，因为服务器可能已不可达）
        try { _signalingClient?.Dispose(); } catch { }
        _signalingClient = null;
    }

    public void Dispose()
    {
        // 同步释放：按正确顺序停止所有组件

        // 0. 停止重连
        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

        // 1. 取消心跳和发送队列
        if (_heartbeatCts != null)
        {
            _heartbeatCts.Cancel();
            _heartbeatCts.Dispose();
            _heartbeatCts = null;
        }
        if (_sendCts != null)
        {
            _sendCts.Cancel();
            _sendCts.Dispose();
            _sendCts = null;
        }

        // 2. 等待后台任务退出（最多 1 秒），防止访问已释放对象
        try { if (_heartbeatTask != null) _heartbeatTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { if (_sendTask != null) _sendTask.Wait(TimeSpan.FromSeconds(1)); } catch { }

        // 3. 停止本地音频
        try { _audioCapture?.Stop(); } catch { }
        try { _audioPlayer?.Stop(); } catch { }
        try { _voiceReceiver?.Stop(); } catch { }

        // 4. 释放资源
        _audioSignal.Dispose();
        _signalingClient?.Dispose();
        _voiceSender?.Dispose();
        _voiceReceiver?.Dispose();
        _audioCapture?.Dispose();
        _audioPlayer?.Dispose();
        _codec?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        // 先执行正常断连流程（发送 LeaveRequest、等待后台任务退出）
        try { await DisconnectAsync(); } catch { }

        if (_heartbeatCts != null)
        {
            _heartbeatCts.Dispose();
            _heartbeatCts = null;
        }
        if (_sendCts != null)
        {
            _sendCts.Dispose();
            _sendCts = null;
        }

        _signalingClient?.Dispose();
        _voiceSender?.Dispose();
        _voiceReceiver?.Dispose();
        _audioCapture?.Dispose();
        _audioPlayer?.Dispose();
        _codec?.Dispose();
        GC.SuppressFinalize(this);
    }
}
