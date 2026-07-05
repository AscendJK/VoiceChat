using System.Windows;
using VoiceChat.App.Services;

namespace VoiceChat.Tests;

public class ThemeServiceTests
{
    private bool SkipIfNoWpf()
    {
        return Application.Current == null;
    }

    [Fact]
    public void ApplyTheme_SetsIsDarkMode()
    {
        if (SkipIfNoWpf()) return; // 非 WPF 环境跳过

        ThemeService.ApplyTheme(true);
        Assert.True(ThemeService.IsDarkMode);

        ThemeService.ApplyTheme(false);
        Assert.False(ThemeService.IsDarkMode);
    }

    [Fact]
    public void ToggleTheme_SwitchesState()
    {
        if (SkipIfNoWpf()) return;

        bool before = ThemeService.IsDarkMode;
        ThemeService.ToggleTheme();
        Assert.Equal(!before, ThemeService.IsDarkMode);

        // 恢复原状态
        ThemeService.ApplyTheme(before);
    }

    [Fact]
    public void ApplyTheme_SavesSetting()
    {
        if (SkipIfNoWpf()) return;

        ThemeService.ApplyTheme(true);
        Assert.True(UserSettings.Instance.IsDarkMode);

        ThemeService.ApplyTheme(false);
        Assert.False(UserSettings.Instance.IsDarkMode);
    }
}
