using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Session;

namespace VoiceChat.App.ViewModels;

public partial class AudioSettingsViewModel : ObservableObject
{
    private RoomHost? _roomHost;
    private RoomClient? _roomClient;

    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> PlaybackDevices { get; } = new();
    public string[] QualityOptions => ["标准 (64kbps)", "高清 (96kbps)", "超清 (128kbps)"];

    private AudioDeviceInfo? _selectedCaptureDevice;
    public AudioDeviceInfo? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set { if (SetProperty(ref _selectedCaptureDevice, value)) ApplyCaptureDevice(); }
    }

    private AudioDeviceInfo? _selectedPlaybackDevice;
    public AudioDeviceInfo? SelectedPlaybackDevice
    {
        get => _selectedPlaybackDevice;
        set { if (SetProperty(ref _selectedPlaybackDevice, value)) ApplyPlaybackDevice(); }
    }

    private float _inputVolume;
    public float InputVolume { get => _inputVolume; set => SetProperty(ref _inputVolume, value); }

    private bool _noiseGateEnabled = true;
    public bool NoiseGateEnabled
    {
        get => _noiseGateEnabled;
        set { if (SetProperty(ref _noiseGateEnabled, value)) ApplyNoiseGateSettings(); }
    }

    private float _noiseGateThreshold = 0.005f;
    public float NoiseGateThreshold
    {
        get => _noiseGateThreshold;
        set { if (SetProperty(ref _noiseGateThreshold, value)) ApplyNoiseGateSettings(); }
    }

    // 用户偏好的音质（只在未连接时可编辑）
    private int _desiredQualityIndex = 2; // 默认超清 128kbps

    // 实际使用的音质（房间内由房主决定，未连接时等于用户偏好）
    private int _actualQualityIndex = 2;

    /// <summary>UI 绑定的音质索引：房间内显示实际音质，未连接时显示用户偏好</summary>
    public int SelectedQualityIndex
    {
        get => _actualQualityIndex;
        set
        {
            // 只有在未连接时才能修改（用户偏好）
            if (_roomHost == null && _roomClient == null && _actualQualityIndex != value)
            {
                // 必须先更新 _actualQualityIndex，再触发通知
                // 否则 WPP 回读 getter 拿到旧值，ComboBox 会显示回旧选项
                _actualQualityIndex = value;
                _desiredQualityIndex = value;
                OnPropertyChanged(nameof(SelectedQualityIndex));
                OnPropertyChanged(nameof(SelectedQuality));
            }
        }
    }

    /// <summary>实际使用的音质参数（创建房间/加入房间时使用）</summary>
    public VoiceQuality SelectedQuality => QualityFromIndex(_actualQualityIndex);

    public bool CanTestLoopback => _roomHost == null && _roomClient == null;

    /// <summary>是否可以修改音质（只有未连接时）</summary>
    public bool CanChangeQuality => _roomHost == null && _roomClient == null;

    /// <summary>状态变化通知 MainViewModel</summary>
    public event Action<string>? StatusChanged;

    public void AttachSession(RoomHost? host, RoomClient? client)
    {
        _roomHost = host;
        _roomClient = client;

        // 确定实际使用的音质
        VoiceQuality? roomQuality = null;

        if (client?.CurrentRoom?.Quality != null)
        {
            // 客户端加入房间：使用房主的音质
            roomQuality = client.CurrentRoom.Quality;
        }
        else if (host?.RoomInfo?.Quality != null)
        {
            // 房主创建房间：使用自身选择的音质
            roomQuality = host.RoomInfo.Quality;
        }

        if (roomQuality != null)
        {
            _actualQualityIndex = IndexFromBitrate(roomQuality.Bitrate);
        }
        else
        {
            // 离开房间：恢复为用户偏好
            _actualQualityIndex = _desiredQualityIndex;
        }

        OnPropertyChanged(nameof(SelectedQualityIndex));
        OnPropertyChanged(nameof(SelectedQuality));
        OnPropertyChanged(nameof(CanTestLoopback));
        OnPropertyChanged(nameof(CanChangeQuality));
        LoopbackTestCommand.NotifyCanExecuteChanged();
    }

    /// <summary>将索引转换为音质配置</summary>
    private static VoiceQuality QualityFromIndex(int index) => index switch
    {
        0 => VoiceQuality.Standard,
        1 => VoiceQuality.HighDefinition,
        _ => VoiceQuality.UltraHigh
    };

    /// <summary>将码率转换为最接近的索引</summary>
    private static int IndexFromBitrate(int bitrate) => bitrate switch
    {
        <= 64000 => 0,   // Standard
        <= 96000 => 1,   // HighDefinition
        _ => 2            // UltraHigh
    };

    public async Task InitializeAsync()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var defaultCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var defaultPlayback = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            var capDevices = AudioCapture.GetCaptureDevices();
            CaptureDevices.Clear();
            foreach (var d in capDevices)
                CaptureDevices.Add(new AudioDeviceInfo { DeviceId = d.ID, FriendlyName = d.FriendlyName?.Trim() ?? "",
                    IsDefault = d.FriendlyName?.Trim() == defaultCapture.FriendlyName, Type = d.DataFlow.ToString() });
            SelectedCaptureDevice = CaptureDevices.FirstOrDefault(d => d.IsDefault) ?? CaptureDevices.FirstOrDefault();

            var pbDevices = AudioPlayer.GetPlaybackDevices();
            PlaybackDevices.Clear();
            foreach (var d in pbDevices)
                PlaybackDevices.Add(new AudioDeviceInfo { DeviceId = d.ID, FriendlyName = d.FriendlyName?.Trim() ?? "",
                    IsDefault = d.FriendlyName?.Trim() == defaultPlayback.FriendlyName, Type = d.DataFlow.ToString() });
            SelectedPlaybackDevice = PlaybackDevices.FirstOrDefault(d => d.IsDefault) ?? PlaybackDevices.FirstOrDefault();

            enumerator.Dispose();
        }
        catch (Exception ex) { StatusChanged?.Invoke($"初始化设备失败: {ex.Message}"); }
    }

    private void ApplyCaptureDevice()
    {
        if (_selectedCaptureDevice == null) return;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(_selectedCaptureDevice.DeviceId);
            _roomHost?.SwitchAudioDevice(device);
            _roomClient?.SwitchAudioDevice(device);
            device.Dispose();
            enumerator.Dispose();
        }
        catch (Exception ex) { StatusChanged?.Invoke($"切换采集设备失败: {ex.Message}"); }
    }

    private void ApplyPlaybackDevice()
    {
        if (_selectedPlaybackDevice == null) return;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(_selectedPlaybackDevice.DeviceId);
            _roomHost?.SwitchPlaybackDevice(device);
            _roomClient?.SwitchPlaybackDevice(device);
            device.Dispose();
            enumerator.Dispose();
        }
        catch (Exception ex) { StatusChanged?.Invoke($"切换播放设备失败: {ex.Message}"); }
    }

    private void ApplyNoiseGateSettings()
    {
        var capture = _roomHost?.GetAudioCapture() ?? _roomClient?.GetAudioCapture();
        if (capture != null)
        {
            capture.NoiseGateEnabled = _noiseGateEnabled;
            capture.NoiseGateThreshold = _noiseGateThreshold;
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var devices = AudioCapture.GetCaptureDevices();
            CaptureDevices.Clear();
            foreach (var d in devices)
                CaptureDevices.Add(new AudioDeviceInfo { DeviceId = d.ID, FriendlyName = d.FriendlyName,
                    IsDefault = d.FriendlyName == defaultDevice.FriendlyName, Type = d.DataFlow.ToString() });
            enumerator.Dispose();
            StatusChanged?.Invoke($"发现 {devices.Count} 个音频设备");
        }
        catch (Exception ex) { StatusChanged?.Invoke($"刷新设备失败: {ex.Message}"); }
    }

    [RelayCommand]
    private void RefreshPlaybackDevices()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            var devices = AudioPlayer.GetPlaybackDevices();
            PlaybackDevices.Clear();
            foreach (var d in devices)
                PlaybackDevices.Add(new AudioDeviceInfo { DeviceId = d.ID, FriendlyName = d.FriendlyName,
                    IsDefault = d.FriendlyName == defaultDevice.FriendlyName, Type = d.DataFlow.ToString() });
            enumerator.Dispose();
            StatusChanged?.Invoke($"发现 {devices.Count} 个播放设备");
        }
        catch (Exception ex) { StatusChanged?.Invoke($"刷新播放设备失败: {ex.Message}"); }
    }

    [RelayCommand(CanExecute = nameof(CanTestLoopback))]
    public async Task LoopbackTestAsync()
    {
        try
        {
            StatusChanged?.Invoke("开始本地测试...");
            int sampleRate = 48000; int frameSize = sampleRate * 20 / 1000;
            using var player = new AudioPlayer { SampleRate = sampleRate, Channels = 1 };
            using var codec = new OpusCodec(sampleRate, 1, 48000);
            player.Initialize(); player.Start();
            short[] testBuffer = new short[frameSize]; double phase = 0; double freq = 440.0;
            var sw = System.Diagnostics.Stopwatch.StartNew(); long nextTick = 0;
            for (int frame = 0; frame < 250; frame++)
            {
                for (int i = 0; i < frameSize; i++) { testBuffer[i] = (short)(16000 * Math.Sin(phase)); phase += 2.0 * Math.PI * freq / sampleRate; }
                var encoded = new byte[codec.MaxPacketSize]; int len = codec.Encode(testBuffer, frameSize, encoded);
                if (len > 0) { var decoded = new short[frameSize]; int d = codec.Decode(encoded, len, decoded, false); if (d > 0) player.AddAudioData("test", decoded, d); }
                nextTick += 200000; while (sw.ElapsedTicks < nextTick) { }
            }
            player.Stop(); StatusChanged?.Invoke("本地测试完成!");
        }
        catch (Exception ex) { StatusChanged?.Invoke($"测试失败: {ex.Message}"); }
    }
}