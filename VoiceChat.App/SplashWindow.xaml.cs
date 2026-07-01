using System.Windows;
using System.Windows.Threading;

namespace VoiceChat.App;

public partial class SplashWindow : Window
{
    private DispatcherTimer _closeTimer;

    public SplashWindow()
    {
        InitializeComponent();

        // 保底：15秒后关闭
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _closeTimer.Tick += (s, e) => SafeClose();
        _closeTimer.Start();
    }

    public void SafeClose()
    {
        if (Dispatcher.HasShutdownStarted) return;
        try
        {
            _closeTimer?.Stop();
            Close();
        }
        catch { }
    }
}
