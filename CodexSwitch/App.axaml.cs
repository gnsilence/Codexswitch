using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CodexSwitch.Services;
using CodexSwitch.ViewModels;
using CodexSwitch.Views;

namespace CodexSwitch;

public partial class App : Application
{
    private TrayMenuController? _trayMenuController;
    private MainWindowViewModel? _viewModel;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyClaudeBootstrapConfig();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startHidden = StartupLaunchOptions.ShouldStartHidden(Environment.GetCommandLineArgs().Skip(1));
            MacDockIconService.ConfigureForWindowVisibility(!startHidden);

            _viewModel = new MainWindowViewModel();
            _trayMenuController = new TrayMenuController(
                this,
                desktop,
                _viewModel,
                ShowMainWindow,
                LoadTrayIcon());

            if (!startHidden)
                ShowMainWindow();

            desktop.ShutdownRequested += async (_, _) =>
            {
                _trayMenuController?.Dispose();
                _trayMenuController = null;
                CloseMainWindow();

                if (_viewModel is not null)
                    await _viewModel.DisposeAsync();
                _viewModel = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyClaudeBootstrapConfig()
    {
        ClaudeBootstrapConfigWriter.TryApplyForCurrentUser();
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            _viewModel is null)
        {
            return;
        }

        MacDockIconService.ConfigureForWindowVisibility(true);

        var mainWindow = _mainWindow;
        if (mainWindow is null)
        {
            mainWindow = new MainWindow
            {
                DataContext = _viewModel
            };
            mainWindow.Closed += OnMainWindowClosed;
            _mainWindow = mainWindow;
            desktop.MainWindow = mainWindow;
        }

        if (!mainWindow.IsVisible)
            mainWindow.Show();

        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;

        mainWindow.Activate();
    }

    private void CloseMainWindow()
    {
        if (_mainWindow is not { } mainWindow)
            return;

        mainWindow.Close();
        ReleaseMainWindow(mainWindow);
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MainWindow mainWindow)
            ReleaseMainWindow(mainWindow);
    }

    private void ReleaseMainWindow(MainWindow mainWindow)
    {
        mainWindow.Closed -= OnMainWindowClosed;
        mainWindow.DataContext = null;

        if (ReferenceEquals(_mainWindow, mainWindow))
        {
            _mainWindow = null;
            MacDockIconService.ConfigureForWindowVisibility(false);
            RequestMemoryTrim();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            ReferenceEquals(desktop.MainWindow, mainWindow))
        {
            desktop.MainWindow = null;
        }
    }

    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CodexSwitch/Assets/favicon.ico"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void RequestMemoryTrim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: true);
    }
}
