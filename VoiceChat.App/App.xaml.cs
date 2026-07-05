using System;
using System.Runtime.InteropServices;
using System.Windows;
using VoiceChat.App.Services;

namespace VoiceChat.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    private TrayIconService? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        timeBeginPeriod(1);

        // 先显示闪屏（立即出现）
        var splash = new SplashWindow();
        splash.Show();

        // 创建主窗口
        var mainWindow = new MainWindow();

        // 创建系统托盘
        _trayIcon = new TrayIconService(
            showMainWindow: () => mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }),
            exitApp: () => mainWindow.Dispatcher.Invoke(async () =>
            {
                _trayIcon?.Hide();
                await mainWindow._viewModel.ShutdownAsync();
                mainWindow._viewModel.Dispose();
                mainWindow.Close();
            })
        );

        // 最小化到托盘
        mainWindow.StateChanged += (s, args) =>
        {
            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.Hide();
                _trayIcon.Show();
            }
        };

        mainWindow.Closing += (s, args) =>
        {
            // 关闭时隐藏到托盘而不是退出
            args.Cancel = true;
            mainWindow.Hide();
            _trayIcon.Show();
        };

        // 主窗口渲染完成后，等设备初始化完毕再关闭闪屏
        mainWindow.ContentRendered += (s, args) =>
        {
            mainWindow._viewModel.Initialization.ContinueWith(_ =>
            {
                try { mainWindow.Dispatcher.Invoke(() => splash.SafeClose()); } catch { }
            }, TaskContinuationOptions.ExecuteSynchronously);
        };

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        timeEndPeriod(1);
        base.OnExit(e);
    }
}
