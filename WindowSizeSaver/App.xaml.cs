using System.Windows;

namespace WindowSizeSaver;

public partial class App : Application
{
    Mutex? _mutex;
    bool _ownsMutex;
    TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "WindowSizeSaver_SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();   // 二重起動防止
            return;
        }

        base.OnStartup(e);
        _tray = new TrayService();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
