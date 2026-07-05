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
    private bool _isExiting;

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
                _isExiting = true;
                _trayIcon?.Hide();
                await mainWindow._viewModel.ShutdownAsync();
                mainWindow._viewModel.Dispose();
                mainWindow.Close();
                Shutdown();
            })
        );

        // 最小化到托盘
        mainWindow.StateChanged += (s, args) =>
        {
            if (mainWindow.WindowState == WindowState.Minimized && !_isExiting)
            {
                mainWindow.Hide();
                _trayIcon.Show();
            }
        };

        mainWindow.Closing += (s, args) =>
        {
            if (!_isExiting)
            {
                // 关闭时隐藏到托盘而不是退出
                args.Cancel = true;
                mainWindow.Hide();
                _trayIcon.Show();
            }
        };

        // 主窗口显示后关闭闪屏
        mainWindow.Loaded += (s, args) =>
        {
            splash.Close();
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
