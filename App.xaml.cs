using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace G321Studio;

public partial class App : Application
{
    private Mutex? instance;
    private StudioTray? tray;
    private bool exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mutex = new Mutex(true, @"Local\G321 Studio", out bool firstInstance);
        if (!firstInstance)
        {
            mutex.Dispose();
            Shutdown();
            return;
        }
        instance = mutex;

        StudioText.Initialize();
        var window = new MainWindow(autoEq: true)
        {
            Language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag)
        };
        MainWindow = window;
        window.Closing += (_, args) =>
        {
            if (exiting)
                return;

            args.Cancel = true;
            window.Hide();
        };
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized)
                window.Hide();
        };
        tray = new StudioTray(ShowMainWindow, ExitFromTray);
        await window.StartAudioMonitoringAsync();
    }

    private void ShowMainWindow()
    {
        if (exiting || MainWindow is not { } window)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    private void ExitFromTray()
    {
        exiting = true;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel)
            exiting = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
