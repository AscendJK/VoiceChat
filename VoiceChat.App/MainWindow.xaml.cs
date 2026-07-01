using System.Windows;
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
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // 异步关闭：先通知对端（RoomDissolved/LeaveRequest），再释放资源
        try { await _viewModel.ShutdownAsync(); } catch { }
        // 释放 ViewModel 资源（停止定时器、释放 RoomSession/RoomHost/RoomClient）
        _viewModel.Dispose();
    }
}
