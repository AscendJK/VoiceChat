using System.ComponentModel;
using VoiceChat.App.ViewModels;

namespace VoiceChat.Tests;

/// <summary>
/// 测试属性变更通知顺序：确保 PropertyChanged 事件触发时，
/// getter 返回的是新值而非旧值（模拟 WPF 绑定的回读行为）。
/// 这是为了捕获类似"ComboBox 选择后又回退"的 UI 绑定 bug。
/// </summary>
public class PropertyChangedOrderTests
{
    /// <summary>
    /// 在 PropertyChanged 事件处理器中立即读取属性值，
    /// 验证读取到的是新值（而非旧值）。
    /// </summary>
    private static int CaptureGetterOnPropertyChanged(
        AudioSettingsViewModel vm,
        string propertyName,
        Action triggerAction)
    {
        int capturedValue = -1;
        bool eventFired = false;

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName)
            {
                // 模拟 WPF 在收到 PropertyChanged 时立即回读 getter
                capturedValue = vm.SelectedQualityIndex;
                eventFired = true;
            }
        }

        vm.PropertyChanged += Handler;
        triggerAction();
        vm.PropertyChanged -= Handler;

        Assert.True(eventFired, $"PropertyChanged 事件未触发: {propertyName}");
        return capturedValue;
    }

    [Fact]
    public void SelectedQualityIndex_WhenChanged_PropertyChangedReturnsNewValue()
    {
        // 模拟 WPF 绑定行为：
        // 1. 用户在 ComboBox 中选择 96kbps (index=1)
        // 2. 绑定系统调用 setter = 1
        // 3. setter 内部触发 PropertyChanged
        // 4. WPF 回读 getter 获取当前显示值
        // 如果 getter 在此时返回旧值，ComboBox 会显示回旧选项

        // 使用反射创建实例（构造函数需要 MainViewModel 上下文）
        var vm = CreateAudioSettingsViewModel();

        // 初始值为 2 (UltraHigh/128kbps)
        Assert.Equal(2, vm.SelectedQualityIndex);

        // 模拟用户选择 96kbps (index=1)
        // 并捕获 PropertyChanged 触发瞬间 getter 的值
        int capturedAtEventFiring = CaptureGetterOnPropertyChanged(
            vm,
            nameof(AudioSettingsViewModel.SelectedQualityIndex),
            () => vm.SelectedQualityIndex = 1);

        // 事件触发时 getter 必须返回新值（1），而非旧值（2）
        Assert.Equal(1, capturedAtEventFiring);
        Assert.Equal(1, vm.SelectedQualityIndex);
    }

    [Fact]
    public void SelectedQualityIndex_Change128To96_WpfReadsNewValue()
    {
        // 真实场景：房主先用 128k 创建房间，解散后改为 96k
        var vm = CreateAudioSettingsViewModel();

        // 128k → 创建房间
        vm.SelectedQualityIndex = 2;
        Assert.Equal(2, vm.SelectedQualityIndex);

        // 解散房间后（模拟 AttachSession(null, null)）
        // UI 恢复为_desired值
        // 用户选择 96k
        int captured = CaptureGetterOnPropertyChanged(
            vm,
            nameof(AudioSettingsViewModel.SelectedQualityIndex),
            () => vm.SelectedQualityIndex = 1);

        // WPF 必须在 PropertyChanged 触发时读到 1（96k），而非 2（128k）
        Assert.Equal(1, captured);
    }

    [Fact]
    public void SelectedQualityIndex_MultipleChanges_AllNotifyNewValue()
    {
        // 连续切换音质，每次 PropertyChanged 都必须反映最新值
        var vm = CreateAudioSettingsViewModel();
        var values = new[] { 0, 2, 1, 0, 2 };

        foreach (int target in values)
        {
            int captured = CaptureGetterOnPropertyChanged(
                vm,
                nameof(AudioSettingsViewModel.SelectedQualityIndex),
                () => vm.SelectedQualityIndex = target);

            Assert.Equal(target, captured);
        }
    }

    [Fact]
    public void CanChangeQuality_WhenConnected_PropertyChangedFires()
    {
        // 验证 AttachSession 时属性变更通知正常触发
        var vm = CreateAudioSettingsViewModel();
        var firedProperties = new List<string>();

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            firedProperties.Add(e.PropertyName ?? string.Empty);
        }

        vm.PropertyChanged += Handler;

        // 触发 AttachSession 模拟离开房间
        vm.AttachSession(null, null);

        vm.PropertyChanged -= Handler;

        // 验证 CanChangeQuality 和 CanTestLoopback 都触发了通知
        Assert.Contains(nameof(AudioSettingsViewModel.CanTestLoopback), firedProperties);
        Assert.Contains(nameof(AudioSettingsViewModel.CanChangeQuality), firedProperties);

        // 验证 CanChangeQuality 为 true（未连接状态）
        Assert.True(vm.CanChangeQuality);
    }

    [Fact]
    public void SelectedQuality_QualityPropertyChanged_FiresAfterIndex()
    {
        // 监听 SelectedQuality 的 PropertyChanged 触发顺序
        // 验证 SelectedQuality 在 Index 之后更新
        var vm = CreateAudioSettingsViewModel();

        var firedProperties = new List<string>();

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            firedProperties.Add(e.PropertyName ?? string.Empty);
        }

        vm.PropertyChanged += Handler;
        vm.SelectedQualityIndex = 1;  // 改为 HighDefinition
        vm.PropertyChanged -= Handler;

        // SelectedQualityIndex 的 PropertyChanged 必须在 SelectedQuality 之前
        int indexPos = firedProperties.IndexOf(nameof(AudioSettingsViewModel.SelectedQualityIndex));
        int qualityPos = firedProperties.IndexOf(nameof(AudioSettingsViewModel.SelectedQuality));

        Assert.True(indexPos >= 0, "SelectedQualityIndex PropertyChanged 未触发");
        Assert.True(qualityPos >= 0, "SelectedQuality PropertyChanged 未触发");
        Assert.True(indexPos < qualityPos,
            $"SelectedQualityIndex ({indexPos}) 应在 SelectedQuality ({qualityPos}) 之前通知");
    }

    /// <summary>
    /// 使用反射创建 AudioSettingsViewModel 实例用于测试
    /// </summary>
    private static AudioSettingsViewModel CreateAudioSettingsViewModel()
    {
        // AudioSettingsViewModel 有公共无参构造函数
        return new AudioSettingsViewModel();
    }
}
