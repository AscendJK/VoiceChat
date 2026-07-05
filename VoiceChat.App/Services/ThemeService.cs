using System.Windows;

namespace VoiceChat.App.Services;

/// <summary>
/// 主题切换服务
/// </summary>
public static class ThemeService
{
    private static readonly Uri LightThemeUri = new("pack://application:,,,/Themes/LightTheme.xaml", UriKind.Absolute);
    private static readonly Uri DarkThemeUri = new("pack://application:,,,/Themes/DarkTheme.xaml", UriKind.Absolute);

    public static bool IsDarkMode { get; private set; }

    public static void ApplyTheme(bool dark)
    {
        IsDarkMode = dark;
        var app = Application.Current;
        if (app == null) return;

        var dict = app.Resources.MergedDictionaries;
        dict.Clear();
        dict.Add(new ResourceDictionary { Source = dark ? DarkThemeUri : LightThemeUri });

        // 保存设置
        UserSettings.Instance.IsDarkMode = dark;
        UserSettings.Instance.Save();
    }

    public static void ToggleTheme()
    {
        ApplyTheme(!IsDarkMode);
    }

    public static void LoadSavedTheme()
    {
        ApplyTheme(UserSettings.Instance.IsDarkMode);
    }
}
