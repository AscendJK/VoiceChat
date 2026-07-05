using System.Windows;
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
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.ToggleTheme();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { await _viewModel.ShutdownAsync(); } catch { }
        _viewModel.Dispose();
    }
}
