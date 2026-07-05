using System.Drawing;
using System.Windows.Forms;

namespace VoiceChat.App.Services;

/// <summary>
/// 系统托盘图标服务
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _trayIcon;
    private readonly Action _showMainWindow;
    private readonly Action _exitApp;

    public TrayIconService(Action showMainWindow, Action exitApp)
    {
        _showMainWindow = showMainWindow;
        _exitApp = exitApp;

        var icon = System.Drawing.Icon.ExtractAssociatedIcon(
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "");

        _trayIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "VoiceChat - 局域网语音通话",
            Visible = false
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (s, e) => _showMainWindow());
        menu.Items.Add("-");
        menu.Items.Add("退出", null, (s, e) => _exitApp());
        _trayIcon.ContextMenuStrip = menu;

        _trayIcon.DoubleClick += (s, e) => _showMainWindow();
    }

    public void Show() => _trayIcon.Visible = true;
    public void Hide() => _trayIcon.Visible = false;

    public void UpdateTooltip(string text)
    {
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
