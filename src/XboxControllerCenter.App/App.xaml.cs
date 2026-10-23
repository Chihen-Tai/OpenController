using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;
using XboxControllerCenter.App.ViewModels;

namespace XboxControllerCenter.App;

public partial class App : Application
{
    private AppServices? _services;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exited;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            FileLog.Write($"Unhandled exception: {args.ExceptionObject}");
            ReleaseOutputsBestEffort();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            FileLog.Write($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        try
        {
            _services = new AppServices();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Xbox Controller Center could not start:\n\n{ex.Message}", "Xbox Controller Center",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Loc.Instance.SetLanguage(_services.Settings.Language);
        ThemeManager.Apply(_services.Settings.Theme);
        var vm = new MainViewModel(_services);
        _window = new MainWindow(_services, vm);
        _tray = new TrayIcon(() => _window.ShowFromTray(), ExitApplication);
        _tray.SetMenuText(Loc.T("Tray.Open"), Loc.T("Tray.Exit"));
        vm.Tray = _tray;
        MainWindow = _window;
        _window.Show();
    }

    /// <summary>Orderly shutdown: neutral outputs, stop threads, save, remove tray icon.</summary>
    public void ExitApplication() => ExitApplication(closeWindow: true);

    /// <param name="closeWindow">False when called from the window's own Closing handler.</param>
    public void ExitApplication(bool closeWindow)
    {
        if (_exited)
            return;
        _exited = true;
        _window?.PrepareExit();
        _tray?.Dispose();
        _services?.Dispose();
        if (closeWindow)
            _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Covers session end / Shutdown() paths that did not go through ExitApplication().
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Write($"UI exception: {e.Exception}");
        ReleaseOutputsBestEffort();
        MessageBox.Show(Loc.T("Error.Unexpected", e.Exception.Message), "Xbox Controller Center",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void ReleaseOutputsBestEffort()
    {
        try
        {
            _services?.Keyboard.ReleaseAll();
        }
        catch (Exception)
        {
            // Last-chance cleanup; nothing else to do.
        }
    }
}
