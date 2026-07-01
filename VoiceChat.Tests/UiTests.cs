using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VoiceChat.Tests;

/// <summary>
/// Shared fixture for UI tests - launches app once, shared across all UI tests.
/// </summary>
public class UiTestFixture : IDisposable
{
    public Process AppProcess { get; private set; }
    public string AppPath { get; }

    public UiTestFixture()
    {
        var path1 = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "publish_slim", "VoiceChat.App.exe");
        var path2 = @"E:\ClaudeCode\VoiceChat\publish_slim\VoiceChat.App.exe";
        AppPath = File.Exists(path1) ? path1 : path2;

        // Kill any existing instances
        foreach (var proc in Process.GetProcessesByName("VoiceChat.App"))
        {
            try { proc.Kill(); } catch { }
        }
        Thread.Sleep(300);

        if (!File.Exists(AppPath))
            throw new FileNotFoundException($"App not found at: {AppPath}");

        AppProcess = Process.Start(new ProcessStartInfo { FileName = AppPath, UseShellExecute = true })
            ?? throw new InvalidOperationException("Failed to start app");

        // Wait for window
        for (int i = 0; i < 50; i++)
        {
            AppProcess.Refresh();
            if (AppProcess.MainWindowHandle != IntPtr.Zero)
            {
                Thread.Sleep(800);
                return;
            }
            Thread.Sleep(200);
        }
        throw new TimeoutException("App window not ready");
    }

    public void Dispose()
    {
        try
        {
            if (AppProcess != null && !AppProcess.HasExited)
            {
                AppProcess.CloseMainWindow();
                if (!AppProcess.WaitForExit(2000))
                    AppProcess.Kill();
                AppProcess.Dispose();
            }
        }
        catch { }

        // Cleanup any stragglers
        foreach (var p in Process.GetProcessesByName("VoiceChat.App"))
        {
            try { p.Kill(); } catch { }
        }
    }
}

[CollectionDefinition("UiTestCollection")]
public class UiTestCollection : ICollectionFixture<UiTestFixture> { }

[Collection("UiTestCollection")]
public class UiTests
{
    private readonly UiTestFixture _fixture;

    public UiTests(UiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private AutomationElement GetMainWindow()
    {
        var handle = _fixture.AppProcess.MainWindowHandle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("No main window");
        return AutomationElement.FromHandle(handle);
    }

    private IEnumerable<AutomationElement> GetAllDescendants()
    {
        return GetMainWindow().FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>();
    }

    private AutomationElement? FindButton(string name)
    {
        return GetAllDescendants()
            .FirstOrDefault(e => e.Current.ControlType == ControlType.Button &&
                                e.Current.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private bool ButtonEnabled(string name)
    {
        var btn = FindButton(name) ?? throw new InvalidOperationException($"Button '{name}' not found");
        return btn.Current.IsEnabled;
    }

    [Fact]
    public void App_HasMainWindow()
    {
        Assert.NotEqual(IntPtr.Zero, _fixture.AppProcess.MainWindowHandle);
    }

    [Fact]
    public void RefreshButton_IsEnabled()
    {
        Assert.True(ButtonEnabled("刷新"));
    }

    [Fact]
    public void CreateRoomButton_IsEnabled()
    {
        Assert.True(ButtonEnabled("创建房间"));
    }

    [Fact]
    public void JoinButton_IsDisabled_NoSelection()
    {
        Assert.False(ButtonEnabled("加入房间"));
    }

    [Fact]
    public void AllInitialButtons_Exist()
    {
        // Buttons visible in initial state (not in collapsed panels)
        Assert.NotNull(FindButton("刷新"));
        Assert.NotNull(FindButton("加入房间"));
        Assert.NotNull(FindButton("创建房间"));
    }

    [Fact]
    public void ConnectionButtons_ExistInTree()
    {
        // Buttons in collapsed panels exist but may not be visible
        // They should be findable in the full tree
        var all = GetAllDescendants()
            .Where(e => e.Current.ControlType == ControlType.Button)
            .Select(e => e.Current.Name)
            .ToList();

        // At minimum, these button texts should exist somewhere in the tree
        Assert.Contains(all, n => n.Contains("刷新"));
        Assert.Contains(all, n => n.Contains("加入"));
        Assert.Contains(all, n => n.Contains("创建"));
    }

    [Fact]
    public void Window_HasTitle()
    {
        Assert.False(string.IsNullOrEmpty(_fixture.AppProcess.MainWindowTitle));
    }
}
