using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace VoiceChat.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        timeBeginPeriod(1);

        // 先显示闪屏（立即出现）
        var splash = new SplashWindow();
        splash.Show();

        // 创建主窗口
        var mainWindow = new MainWindow();

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
        timeEndPeriod(1);
        base.OnExit(e);
    }
}
