using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using VoiceChat.App.Services;
using VoiceChat.App.ViewModels;

namespace VoiceChat.App;

public partial class MainWindow : Window
{
    internal readonly MainViewModel _viewModel;
    private bool _pttKeyDown;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Push-to-Talk 键盘事件
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.AudioSettings.PushToTalkEnabled) return;
        if (_viewModel.RoomSession.IsMuted) return;

        // 获取实际按键名（处理 LeftCtrl vs RightCtrl）
        var keyName = e.Key == Key.System ? e.SystemKey.ToString() : e.Key.ToString();
        if (keyName == _viewModel.AudioSettings.PushToTalkKey && !_pttKeyDown)
        {
            _pttKeyDown = true;
            _viewModel.AudioSettings.OnPushToTalkKeyDown();
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_viewModel.AudioSettings.PushToTalkEnabled) return;

        var keyName = e.Key == Key.System ? e.SystemKey.ToString() : e.Key.ToString();
        if (keyName == _viewModel.AudioSettings.PushToTalkKey && _pttKeyDown)
        {
            _pttKeyDown = false;
            _viewModel.AudioSettings.OnPushToTalkKeyUp();
            e.Handled = true;
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.ToggleTheme();
    }

    protected override async void OnClosed(EventArgs e)
    {
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        PreviewKeyUp -= MainWindow_PreviewKeyUp;
        base.OnClosed(e);
        try { await _viewModel.ShutdownAsync(); } catch { }
        _viewModel.Dispose();
    }
}
