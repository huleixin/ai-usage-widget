using System.Windows;

namespace CodexUsageWidgetApp;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _settingsEvent;
    private RegisteredWaitHandle? _registration;
    internal bool ShowSettingsOnStart { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\AIUsageWidget.Instance", out bool created);
        if (!created)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\AIUsageWidget.Settings"); signal.Set(); } catch { }
            Shutdown(); return;
        }
        _settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsageWidget.Settings");
        _registration = ThreadPool.RegisterWaitForSingleObject(_settingsEvent, (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.OpenSettings())), null, Timeout.Infinite, false);
        ShowSettingsOnStart = e.Args.Contains("--settings") || !System.IO.File.Exists(WidgetSettings.FilePath);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _registration?.Unregister(null);
        _settingsEvent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
