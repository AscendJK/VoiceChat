using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceChat.Core.Models;
using VoiceChat.Core.Network;
using VoiceChat.Core.Session;
using VoiceChat.Core.Audio;
using NAudio.CoreAudioApi;

namespace VoiceChat.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public AudioSettingsViewModel AudioSettings { get; } = new();
    public RoomSessionViewModel RoomSession { get; } = new();
    public NetworkStatsViewModel NetworkStats { get; } = new();

    private RoomHost? _roomHost;
    private RoomClient? _roomClient;
    private UdpBroadcasterScanner? _scanner;
    private DispatcherTimer? _statsTimer;
    /// <summary>
    /// 异步初始化任务（设备枚举等），供启动时等待
    /// </summary>
    public Task Initialization { get; private set; } = Task.CompletedTask;
    private DispatcherTimer? _syncTimer;
    private string _statusText = "就绪";
    private bool _isConnected;
    private bool _isHost;
    private bool _isMuted;
    private RoomInfo? _selectedRoom;
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsConnected { get => _isConnected; set { _isConnected = value; OnPropertyChanged(); UpdateCommands(); } }
    public bool IsHost { get => _isHost; set { _isHost = value; OnPropertyChanged(); } }
    public bool IsMuted { get => _isMuted; set { _isMuted = value; OnPropertyChanged(); } }
    public RoomInfo? SelectedRoom { get => _selectedRoom; set { _selectedRoom = value; OnPropertyChanged(); UpdateCommands(); } }

    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanJoinRoom));
        OnPropertyChanged(nameof(CanRefreshRooms));
        JoinRoomCommand.NotifyCanExecuteChanged();
        RefreshRoomsCommand.NotifyCanExecuteChanged();
    }
    public ObservableCollection<RoomInfo> DiscoveredRooms { get; } = new();

    public bool CanJoinRoom => !RoomSession.IsConnected && SelectedRoom != null;
    public bool CanRefreshRooms => !RoomSession.IsConnected;

    private volatile bool _disposed;

    // 缓存事件处理程序引用，确保能正确取消订阅
    private readonly Action? _connectionStateChangedHandler;
    private readonly Action<RoomHost?, RoomClient?>? _audioContextChangedHandler;
    private readonly Action<float>? _inputVolumeChangedHandler;
    private readonly PropertyChangedEventHandler? _audioSettingsPropertyChangedHandler;
    private Action<string>? _statusChangedHandler;

    public MainViewModel()
    {
        Initialization = InitializeAsync();

        // 监听 RoomSession 事件（缓存处理程序引用以便取消订阅）
        _connectionStateChangedHandler = () => SafePostToDispatcher(() =>
        {
            UpdateScannerState();
            UpdateCommands();
        });
        RoomSession.ConnectionStateChanged += _connectionStateChangedHandler;

        _audioContextChangedHandler = (host, client) =>
        {
            _roomHost = host;
            _roomClient = client;
            AudioSettings.AttachSession(host, client);

            // 更新网络统计
            StartStatsTimer();
        };
        RoomSession.AudioContextChanged += _audioContextChangedHandler;

        _inputVolumeChangedHandler = volume => SafePostToDispatcher(() => AudioSettings.InputVolume = volume);
        RoomSession.InputVolumeChanged += _inputVolumeChangedHandler;

        // 同步音质选择：只在未连接时同步（用户偏好 → 房间创建用）
        _audioSettingsPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(AudioSettings.SelectedQualityIndex) && !RoomSession.IsConnected)
            {
                RoomSession.SelectedQualityIndex = AudioSettings.SelectedQualityIndex;
            }
        };
        AudioSettings.PropertyChanged += _audioSettingsPropertyChangedHandler;

        // 初始化命令状态
        UpdateCommands();

        // 定时同步成员列表（每1秒），防止成员离开后UI残留
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Start();
    }

    public void Dispose()
    {
        // 先设置 disposed 标志（防止定时器 Tick 访问已释放对象）
        _disposed = true;

        // 取消所有事件订阅（防止内存泄漏）
        UnsubscribeEvents();

        // 停止同步定时器
        if (_syncTimer != null)
        {
            _syncTimer.Stop();
            _syncTimer = null;
        }

        // 先关闭扫描器（不阻塞）
        _scanner?.Dispose();
        _scanner = null;

        // Dispose 会处理完整清理（停止任务、取消事件、释放资源）
        // 不需要先调用 CloseAsync，避免双重释放
        try { _roomHost?.Dispose(); } catch { }
        try { _roomClient?.Dispose(); } catch { }
        _roomHost = null;
        _roomClient = null;

        RoomSession?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// 异步关闭（在 UI 线程上调用，先通知对端再释放资源）
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 取消事件订阅
        UnsubscribeEvents();

        // 停止定时器和扫描器
        if (_syncTimer != null)
        {
            _syncTimer.Stop();
            _syncTimer = null;
        }
        _scanner?.Dispose();
        _scanner = null;

        // 异步关闭房间（发送 RoomDissolved/LeaveRequest）
        var tasks = new List<Task>();
        if (_roomHost != null) tasks.Add(_roomHost.CloseAsync());
        if (_roomClient != null) tasks.Add(_roomClient.CloseAsync());

        if (tasks.Count > 0)
        {
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { }
        }

        // 释放资源
        try { _roomHost?.Dispose(); } catch { }
        try { _roomClient?.Dispose(); } catch { }
        _roomHost = null;
        _roomClient = null;

        RoomSession?.Dispose();
    }

    private void StartStatsTimer()
    {
        _statsTimer?.Stop();
        if (_roomHost == null && _roomClient == null)
        {
            NetworkStats.Reset();
            NetworkStats.IsStatsVisible = false;
            return;
        }

        NetworkStats.IsStatsVisible = true;
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) =>
        {
            if (_roomHost != null)
            {
                var sender = _roomHost.GetAudioCapture();
                // 从 VoiceSender 获取统计（如果有的话）
                // 目前简化处理，只显示基本状态
                NetworkStats.UpdateStats(0, 0, 0, 0, 0);
            }
            else if (_roomClient != null)
            {
                NetworkStats.UpdateStats(0, 0, 0, 0, 0);
            }
        };
        _statsTimer.Start();
    }

    private void UnsubscribeEvents()
    {
        _statsTimer?.Stop();
        _statsTimer = null;
        NetworkStats.IsStatsVisible = false;
        if (_connectionStateChangedHandler != null)
            RoomSession.ConnectionStateChanged -= _connectionStateChangedHandler;
        if (_audioContextChangedHandler != null)
            RoomSession.AudioContextChanged -= _audioContextChangedHandler;
        if (_inputVolumeChangedHandler != null)
            RoomSession.InputVolumeChanged -= _inputVolumeChangedHandler;
        if (_audioSettingsPropertyChangedHandler != null)
            AudioSettings.PropertyChanged -= _audioSettingsPropertyChangedHandler;
        if (_statusChangedHandler != null)
            AudioSettings.StatusChanged -= _statusChangedHandler;
    }

    public async Task InitializeAsync()
    {
        await Task.Delay(1);
        await AudioSettings.InitializeAsync();
        _statusChangedHandler = msg => StatusText = msg;
        AudioSettings.StatusChanged += _statusChangedHandler;

        // 初始同步：确保 RoomSession 的音质索引与 AudioSettings 一致
        RoomSession.SelectedQualityIndex = AudioSettings.SelectedQualityIndex;
    }

    private void StopScanner() { _scanner?.Dispose(); _scanner = null; }
    private void UpdateScannerState()
    {
        if (RoomSession.IsConnected) StopScanner();
    }

    [RelayCommand(CanExecute = nameof(CanJoinRoom))]
    private async Task JoinRoomAsync()
    {
        if (SelectedRoom == null || string.IsNullOrWhiteSpace(RoomSession.UserName)) { StatusText = "请选择房间"; return; }
        try
        {
            StopScanner();

            // 密码房间需要输入密码
            string? password = null;
            if (SelectedRoom.HasPassword)
            {
                password = ShowPasswordDialog(SelectedRoom.Name);
                if (password == null) return; // 用户取消
            }

            await RoomSession.JoinRoomAsync(SelectedRoom, RoomSession.UserName, password);
        }
        catch (Exception ex) { StatusText = $"加入失败: {ex.Message}"; }
    }

    private string? ShowPasswordDialog(string roomName)
    {
        var dialog = new System.Windows.Window
        {
            Title = $"加入房间 - {roomName}",
            Width = 350,
            Height = 180,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "该房间需要密码",
            FontSize = 14,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        });

        var passwordBox = new System.Windows.Controls.PasswordBox
        {
            FontSize = 14,
            Padding = new System.Windows.Thickness(8, 4, 8, 4),
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(passwordBox);

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        string? result = null;

        var okBtn = new System.Windows.Controls.Button
        {
            Content = "确定",
            Width = 80,
            Height = 30,
            Margin = new System.Windows.Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (s, e) => { result = passwordBox.Password; dialog.DialogResult = true; };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "取消",
            Width = 80,
            Height = 30,
            IsCancel = true
        };
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(btnPanel);

        dialog.Content = stack;
        passwordBox.Focus();

        return dialog.ShowDialog() == true ? result : null;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshRooms))]
    private async Task RefreshRoomsAsync()
    {
        try
        {
            StatusText = "正在扫描...";
            DiscoveredRooms.Clear();
            _scanner?.Dispose();
            _scanner = new UdpBroadcasterScanner();
            _scanner.OnRoomDiscovered += room => SafeInvokeDispatcher(() => { if (!DiscoveredRooms.Any(r => r.Id == room.Id)) DiscoveredRooms.Add(room); });
            _scanner.OnRoomUpdated += room => SafeInvokeDispatcher(() => { var existing = DiscoveredRooms.FirstOrDefault(r => r.Id == room.Id); if (existing != null) existing.UpdateFrom(room); });
            _scanner.OnRoomExpired += roomId => SafeInvokeDispatcher(() => { var room = DiscoveredRooms.FirstOrDefault(r => r.Id == roomId); if (room != null) DiscoveredRooms.Remove(room); });
            _scanner.OnError += msg => SafeInvokeDispatcher(() => StatusText = msg);
            _scanner.Start();
            if (!_scanner.IsRunning) { StatusText = "扫描器启动失败（端口 9999 被占用）"; return; }
            await _scanner.ProbeAsync();
            await Task.Delay(2000);
            StopScanner();
            StatusText = DiscoveredRooms.Count > 0 ? $"发现 {DiscoveredRooms.Count} 个房间" : "未发现房间";
        }
        catch (Exception ex) { StatusText = $"扫描失败: {ex.Message}"; }
    }

    private void OnSyncTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || !RoomSession.IsConnected) return;

        // 同步成员列表：移除已不在服务器列表中的成员（防止成员离开后 UI 残留）
        try
        {
            var serverMemberIds = new HashSet<string>();
            if (RoomSession.RoomHost != null)
            {
                foreach (var m in RoomSession.RoomHost.Members)
                    serverMemberIds.Add(m.Id);
            }
            else if (RoomSession.RoomClient != null)
            {
                foreach (var m in RoomSession.RoomClient.Members)
                    serverMemberIds.Add(m.Id);
            }

            // 找出 UI 中存在但服务器列表中已不存在的成员
            var toRemove = RoomSession.Members.Where(m => !serverMemberIds.Contains(m.Id)).ToList();
            if (toRemove.Count > 0)
            {
                SafePostToDispatcher(() =>
                {
                    foreach (var vm in toRemove)
                    {
                        RoomSession.MembersDict.Remove(vm.Id);
                        RoomSession.Members.Remove(vm);
                    }
                });
            }
        }
        catch { }
    }

    private void RemoveDiscoveredRoom(string? roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        var room = DiscoveredRooms.FirstOrDefault(r => r.Id == roomId);
        if (room != null) DiscoveredRooms.Remove(room);
    }

    private static void SafeInvokeDispatcher(Action action) { try { var d = Application.Current?.Dispatcher; if (d != null && !d.HasShutdownStarted) d.Invoke(action); } catch { } }

    private static void SafePostToDispatcher(Action action) { try { var d = Application.Current?.Dispatcher; if (d != null && !d.HasShutdownStarted) d.BeginInvoke(action); } catch { } }
}

public class RoomMemberViewModel : INotifyPropertyChanged, IDisposable
{
    private bool _isMuted;
    private bool _isMutedByMe;
    private float _volume = 1.0f;
    private bool _isSpeaking;
    private readonly DispatcherTimer _speakTimer;
    public string Id { get; }
    public string Name { get; }
    public bool IsHost { get; }
    public bool IsMuted { get => _isMuted; set { _isMuted = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public bool IsMutedByMe { get => _isMutedByMe; set { _isMutedByMe = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); } }
    public float Volume { get => _volume; set { _volume = value; OnPropertyChanged(); OnVolumeChanged?.Invoke(value); } }
    public bool IsSpeaking { get => _isSpeaking; set { _isSpeaking = value; OnPropertyChanged(); } }
    public string StatusText
    {
        get
        {
            var p = new List<string>(3);
            if (IsHost) p.Add("房主");
            if (IsMuted) p.Add("已静音");
            if (IsMutedByMe) p.Add("被屏蔽");
            return p.Count > 0 ? $" ({string.Join(", ", p)})" : "";
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Action<float>? OnVolumeChanged;
    public RoomMemberViewModel(RoomMember member)
    {
        Id = member.Id;
        Name = member.Name;
        IsHost = member.IsHost;
        IsMuted = member.IsMuted;
        IsMutedByMe = member.IsMutedByMe;
        Volume = member.Volume;
        _speakTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _speakTimer.Tick += (s, e) => { _speakTimer.Stop(); IsSpeaking = false; };
    }
    public void MarkSpeaking() { IsSpeaking = true; _speakTimer.Stop(); _speakTimer.Start(); }
    public void Dispose() { _speakTimer?.Stop(); }
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
}