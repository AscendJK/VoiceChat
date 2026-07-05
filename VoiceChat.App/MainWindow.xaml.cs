using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using VoiceChat.App.Services;
using VoiceChat.App.ViewModels;

namespace VoiceChat.App;

public partial class MainWindow : Window
{
    internal readonly MainViewModel _viewModel;

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
        if (_viewModel.AudioSettings.PushToTalkEnabled && !_viewModel.RoomSession.IsMuted)
        {
            var keyName = e.Key.ToString();
            if (keyName == _viewModel.AudioSettings.PushToTalkKey)
            {
                _viewModel.AudioSettings.OnPushToTalkKeyDown();
                e.Handled = true;
            }
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_viewModel.AudioSettings.PushToTalkEnabled)
        {
            var keyName = e.Key.ToString();
            if (keyName == _viewModel.AudioSettings.PushToTalkKey)
            {
                _viewModel.AudioSettings.OnPushToTalkKeyUp();
                e.Handled = true;
            }
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
        // 异步关闭：先通知对端（RoomDissolved/LeaveRequest），再释放资源
        try { await _viewModel.ShutdownAsync(); } catch { }
        // 释放 ViewModel 资源（停止定时器、释放 RoomSession/RoomHost/RoomClient）
        _viewModel.Dispose();
    }
}
