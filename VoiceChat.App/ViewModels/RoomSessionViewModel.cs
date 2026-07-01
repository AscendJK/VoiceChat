using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;
using VoiceChat.Core.Session;

namespace VoiceChat.App.ViewModels;

public partial class RoomSessionViewModel : ObservableObject, IDisposable
{
    private RoomHost? _roomHost;
    private RoomClient? _roomClient;

    // 缓存 RoomHost 事件处理程序引用，确保能正确取消订阅
    private Action<RoomMember>? _hostOnMemberJoined;
    private Action<string>? _hostOnMemberLeft;
    private Action<string>? _hostOnUserSpeaking;
    private Action<float>? _hostOnInputVolumeChanged;
    private Action<string>? _hostOnError;

    // 缓存 RoomClient 事件处理程序引用
    private Action<RoomInfo>? _clientOnConnected;
    private Action? _clientOnDisconnected;
    private Action<RoomMember>? _clientOnMemberJoined;
    private Action<string>? _clientOnMemberLeft;
    private Action<string>? _clientOnUserSpeaking;
    private Action<float>? _clientOnInputVolumeChanged;
    private Action? _clientOnRoomDissolved;
    private Action<string>? _clientOnError;

    public RoomHost? RoomHost => _roomHost;
    public RoomClient? RoomClient => _roomClient;

    public ObservableCollection<RoomMemberViewModel> Members { get; } = new();

    private void AddMember(RoomMember member)
    {
        if (MembersDict.ContainsKey(member.Id)) return;
        var vm = new RoomMemberViewModel(member);
        vm.OnVolumeChanged = volume =>
        {
            _roomHost?.SetUserVolume(member.Id, volume);
            _roomClient?.SetUserVolume(member.Id, volume);
        };
        MembersDict[member.Id] = vm;
        Members.Add(vm);
    }
    public readonly Dictionary<string, RoomMemberViewModel> MembersDict = new();

    private string _roomName = $"{Environment.UserName}的房间";
    public string RoomName { get => _roomName; set => SetProperty(ref _roomName, value); }

    private string _userName = Environment.UserName;
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

    // 房间音质（由 MainViewModel 从 AudioSettings 同步）
    private int _selectedQualityIndex = 2;
    public int SelectedQualityIndex
    {
        get => _selectedQualityIndex;
        set => SetProperty(ref _selectedQualityIndex, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanCreateRoom));
                OnPropertyChanged(nameof(CanLeaveRoom));
                OnPropertyChanged(nameof(CanDissolveRoom));
                OnPropertyChanged(nameof(CanToggleMute));
                CreateRoomCommand.NotifyCanExecuteChanged();
                LeaveRoomCommand.NotifyCanExecuteChanged();
                DissolveRoomCommand.NotifyCanExecuteChanged();
                ToggleMuteSelfCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isHost;
    public bool IsHost
    {
        get => _isHost;
        set
        {
            if (SetProperty(ref _isHost, value))
            {
                OnPropertyChanged(nameof(CanLeaveRoom));
                OnPropertyChanged(nameof(CanDissolveRoom));
                LeaveRoomCommand.NotifyCanExecuteChanged();
                DissolveRoomCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isMuted;
    public bool IsMuted { get => _isMuted; set => SetProperty(ref _isMuted, value); }

    private string _connectionStatus = "未连接";
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }

    public bool CanCreateRoom => !IsConnected;
    public bool CanJoinRoom => false; // overridden externally
    public bool CanLeaveRoom => IsConnected && !IsHost;
    public bool CanDissolveRoom => IsConnected && IsHost;
    public bool CanToggleMute => IsConnected;

    /// <summary>状态变化通知 MainViewModel</summary>
    public event Action<string>? StatusChanged;
    public event Action? ConnectionStateChanged;
    /// <summary>房间创建/加入时触发，传递当前音频管理器</summary>
    public event Action<RoomHost?, RoomClient?>? AudioContextChanged;
    /// <summary>输入音量变化时触发</summary>
    public event Action<float>? InputVolumeChanged;

    public void Dispose()
    {
        // 先取消事件订阅，防止 Dispose 后仍有回调
        UnsubscribeEvents();
        _roomHost?.Dispose();
        _roomClient?.Dispose();
    }

    private int _isCreatingRoom; // 防止双击创建房间

    [RelayCommand(CanExecute = nameof(CanCreateRoom))]
    private async Task CreateRoomAsync()
    {
        // 防止快速双击导致重复创建
        if (Interlocked.CompareExchange(ref _isCreatingRoom, 1, 0) != 0) return;

        try
        {
            StatusChanged?.Invoke("正在创建房间...");
            var quality = SelectedQualityIndex switch
            {
                0 => VoiceQuality.Standard,
                1 => VoiceQuality.HighDefinition,
                _ => VoiceQuality.UltraHigh
            };
            _roomHost = new RoomHost();
            _hostOnMemberJoined = (member) => SafePostToDispatcher(() =>
            {
                if (!MembersDict.ContainsKey(member.Id)) AddMember(member);
            });
            _hostOnMemberLeft = (memberId) => SafePostToDispatcher(() =>
            {
                if (MembersDict.TryGetValue(memberId, out var vm))
                {
                    MembersDict.Remove(memberId);
                    Members.Remove(vm);
                }
            });
            _hostOnUserSpeaking = userId => SafePostToDispatcher(() =>
            {
                if (MembersDict.TryGetValue(userId, out var vm)) vm.MarkSpeaking();
            });
            _hostOnInputVolumeChanged = volume => SafePostToDispatcher(() => InputVolumeChanged?.Invoke(volume));
            _hostOnError = error => SafePostToDispatcher(() => StatusChanged?.Invoke(error));

            _roomHost.OnMemberJoined += _hostOnMemberJoined;
            _roomHost.OnMemberLeft += _hostOnMemberLeft;
            _roomHost.OnUserSpeaking += _hostOnUserSpeaking;
            _roomHost.OnInputVolumeChanged += _hostOnInputVolumeChanged;
            _roomHost.OnError += _hostOnError;

            var success = await _roomHost.CreateAsync(RoomName, UserName, quality: quality);
            if (success)
            {
                IsHost = true;
                IsConnected = true;
                IsMuted = false;
                ConnectionStatus = $"已创建房间：{RoomName}";
                ConnectionStateChanged?.Invoke();
                AudioContextChanged?.Invoke(_roomHost, null);
            }
            else
            {
                // 创建失败，释放已分配的资源（防止 WASAPI/网络资源泄漏）
                _roomHost.Dispose();
                _roomHost = null;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"创建房间失败: {ex.Message}");
            // 异常时也要释放资源
            _roomHost?.Dispose();
            _roomHost = null;
        }
        finally
        {
            // 重置创建标志，允许再次创建
            Interlocked.Exchange(ref _isCreatingRoom, 0);
        }
    }

    public async Task JoinRoomAsync(RoomInfo room, string userName, string? password)
    {
        if (room == null) return;
        try
        {
            StatusChanged?.Invoke("正在加入房间...");
            _roomClient = new RoomClient();
            _clientOnConnected = (r) => SafePostToDispatcher(() =>
            {
                IsConnected = true;
                IsHost = false;
                IsMuted = false;
                ConnectionStatus = $"已加入房间：{r.Name}";
                // 加入成功后，立即将服务端返回的成员列表（含房主和其他人）加入 UI
                SeedMembersFromClient();
                ConnectionStateChanged?.Invoke();
                AudioContextChanged?.Invoke(null, _roomClient);
            });
            _clientOnDisconnected = () => SafePostToDispatcher(CleanupConnection);
            _clientOnMemberJoined = (member) => SafePostToDispatcher(() =>
            {
                if (!MembersDict.ContainsKey(member.Id)) AddMember(member);
            });
            _clientOnMemberLeft = (memberId) => SafePostToDispatcher(() =>
            {
                if (MembersDict.TryGetValue(memberId, out var vm))
                {
                    MembersDict.Remove(memberId);
                    Members.Remove(vm);
                }
            });
            _clientOnUserSpeaking = userId => SafePostToDispatcher(() =>
            {
                if (MembersDict.TryGetValue(userId, out var vm)) vm.MarkSpeaking();
            });
            _clientOnInputVolumeChanged = volume => SafePostToDispatcher(() => InputVolumeChanged?.Invoke(volume));
            _clientOnRoomDissolved = () => SafePostToDispatcher(() =>
            {
                StatusChanged?.Invoke("房间已解散");
                CleanupConnection();
            });
            _clientOnError = error => SafePostToDispatcher(() => StatusChanged?.Invoke(error));

            _roomClient.OnConnected += _clientOnConnected;
            _roomClient.OnDisconnected += _clientOnDisconnected;
            _roomClient.OnMemberJoined += _clientOnMemberJoined;
            _roomClient.OnMemberLeft += _clientOnMemberLeft;
            _roomClient.OnUserSpeaking += _clientOnUserSpeaking;
            _roomClient.OnInputVolumeChanged += _clientOnInputVolumeChanged;
            _roomClient.OnRoomDissolved += _clientOnRoomDissolved;
            _roomClient.OnError += _clientOnError;

            var success = await _roomClient.ConnectAsync(room, userName, password);
            if (!success)
            {
                StatusChanged?.Invoke("加入房间失败");
                _roomClient.Dispose();
                _roomClient = null;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"加入房间失败: {ex.Message}");
            _roomClient?.Dispose();
            _roomClient = null;
        }
    }

    /// <summary>
    /// 加入房间后，将服务端返回的成员列表（含房主和其他人）加入 UI
    /// </summary>
    private void SeedMembersFromClient()
    {
        if (_roomClient == null) return;

        // 添加房主（排除自己）
        var host = _roomClient.HostMember;
        if (host != null && host.Id != _roomClient.MemberId && !MembersDict.ContainsKey(host.Id))
        {
            AddMember(host);
        }

        // 添加所有已有成员（排除自己和房主，避免重复）
        foreach (var m in _roomClient.Members)
        {
            if (m.Id != _roomClient.MemberId && !MembersDict.ContainsKey(m.Id))
            {
                AddMember(m);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLeaveRoom))]
    private async Task LeaveRoomAsync()
    {
        if (_roomClient != null)
        {
            await _roomClient.DisconnectAsync();
            _roomClient.Dispose();
            _roomClient = null;
        }
        CleanupConnection();
        StatusChanged?.Invoke("已离开房间");
    }

    [RelayCommand(CanExecute = nameof(CanDissolveRoom))]
    private async Task DissolveRoomAsync()
    {
        if (_roomHost != null)
        {
            await _roomHost.CloseAsync();
            _roomHost.Dispose();
            _roomHost = null;
        }
        CleanupConnection();
        StatusChanged?.Invoke("房间已解散");
    }

    [RelayCommand(CanExecute = nameof(CanToggleMute))]
    private async Task ToggleMuteSelfAsync()
    {
        IsMuted = !IsMuted;
        if (_roomHost != null)
            _roomHost.MuteSelf(IsMuted);
        else if (_roomClient != null)
            await _roomClient.MuteSelfAsync(IsMuted);
    }

    [RelayCommand]
    public async Task KickMemberAsync(string? memberId)
    {
        if (_roomHost != null && !string.IsNullOrEmpty(memberId))
        {
            await _roomHost.KickMemberAsync(memberId);
        }
    }

    public void SetUserVolume(string userId, float volume)
    {
        _roomHost?.SetUserVolume(userId, volume);
        _roomClient?.SetUserVolume(userId, volume);
    }

    public void MuteOther(string memberId, bool mute)
    {
        if (_roomHost != null)
            _roomHost.MuteMember(memberId, mute);
        else
            _roomClient?.MuteOther(memberId, mute);
    }

    public AudioCapture? GetAudioCapture() => _roomHost?.GetAudioCapture() ?? _roomClient?.GetAudioCapture();

    /// <summary>
    /// 取消所有事件订阅（防止内存泄漏）
    /// </summary>
    private void UnsubscribeEvents()
    {
        if (_roomHost != null)
        {
            if (_hostOnMemberJoined != null) _roomHost.OnMemberJoined -= _hostOnMemberJoined;
            if (_hostOnMemberLeft != null) _roomHost.OnMemberLeft -= _hostOnMemberLeft;
            if (_hostOnUserSpeaking != null) _roomHost.OnUserSpeaking -= _hostOnUserSpeaking;
            if (_hostOnInputVolumeChanged != null) _roomHost.OnInputVolumeChanged -= _hostOnInputVolumeChanged;
            if (_hostOnError != null) _roomHost.OnError -= _hostOnError;
        }
        if (_roomClient != null)
        {
            if (_clientOnConnected != null) _roomClient.OnConnected -= _clientOnConnected;
            if (_clientOnDisconnected != null) _roomClient.OnDisconnected -= _clientOnDisconnected;
            if (_clientOnMemberJoined != null) _roomClient.OnMemberJoined -= _clientOnMemberJoined;
            if (_clientOnMemberLeft != null) _roomClient.OnMemberLeft -= _clientOnMemberLeft;
            if (_clientOnUserSpeaking != null) _roomClient.OnUserSpeaking -= _clientOnUserSpeaking;
            if (_clientOnInputVolumeChanged != null) _roomClient.OnInputVolumeChanged -= _clientOnInputVolumeChanged;
            if (_clientOnRoomDissolved != null) _roomClient.OnRoomDissolved -= _clientOnRoomDissolved;
            if (_clientOnError != null) _roomClient.OnError -= _clientOnError;
        }
    }

    private int _cleanupState; // 0=未清理, 1=清理中
    private void CleanupConnection()
    {
        // 使用 Interlocked 防止多线程重复清理
        if (Interlocked.CompareExchange(ref _cleanupState, 1, 0) != 0) return;

        try
        {
            // 取消事件订阅（防止已释放对象的事件回调）
            UnsubscribeEvents();

            Members.Clear();
            MembersDict.Clear();
            IsConnected = false;
            IsHost = false;
            ConnectionStatus = "未连接";
            ConnectionStateChanged?.Invoke();

            // 通知 AudioSettingsViewModel 会话已结束，可以重新启用环回测试
            AudioContextChanged?.Invoke(null, null);
        }
        finally
        {
            // 无论是否异常，都重置清理状态，允许下次连接时再次清理
            Interlocked.Exchange(ref _cleanupState, 0);
        }
    }

    private void SafePostToDispatcher(Action action)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            if (dispatcher.CheckAccess() == true)
                action();
            else
                // 使用 BeginInvoke 而非 Invoke，避免在已持有锁的线程上同步等待 UI 线程导致死锁
                dispatcher.BeginInvoke(action);
        }
        catch { }
    }
}