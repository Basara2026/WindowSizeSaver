using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace WindowSizeSaver;

/// <summary>常駐アイコン・右クリックメニュー・ホットキー・ウインドウ監視をまとめて管理</summary>
sealed class TrayService : IDisposable
{
    const int HotkeyId = 1;
    const uint VK_S = 0x53;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly System.Windows.Forms.NotifyIcon _icon;
    readonly Watcher _watcher = new();
    readonly HwndSource _hotkeyWindow;
    readonly Window _menuOwner;
    SettingsWindow? _settings;
    readonly System.Drawing.Icon _trayIcon = LoadTrayIcon();

    static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
            if (info != null)
            {
                using var stream = info.Stream;
                // 通知領域に合った大きさの画像を ico の中から選ぶ
                return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    public TrayService()
    {
        // メニューを正しく閉じるために必要な、画面外の見えないウインドウ
        _menuOwner = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000
        };

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "WindowSizeSaver",
            Visible = true
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right) ShowMenu();
            else if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowSettings();
        };

        // ホットキー受信用のメッセージ専用ウインドウ
        var p = new HwndSourceParameters("WindowSizeSaverHotkey") { ParentWindow = HWND_MESSAGE };
        _hotkeyWindow = new HwndSource(p);
        _hotkeyWindow.AddHook(WndProc);

        if (!Native.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT, VK_S))
        {
            MessageBox.Show("ホットキー Ctrl+Alt+Shift+S を登録できませんでした。\n他のソフトが使用している可能性があります。",
                "WindowSizeSaver");
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            OnHotkey();
            handled = true;
        }
        return IntPtr.Zero;
    }

    void OnHotkey()
    {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return;
        string proc = Native.GetProcessName(h);
        if (proc == "") return;

        var r = Native.GetVisibleRect(h);
        RuleStore.Register(proc, Native.GetTitle(h),
            r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    void ShowMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        var settings = new MenuItem { Header = "設定" };
        settings.Click += (_, _) => ShowSettings();
        var exit = new MenuItem { Header = "閉じる" };
        exit.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(settings);
        menu.Items.Add(exit);
        menu.Closed += (_, _) => _menuOwner.Hide();

        _menuOwner.Show();
        Native.SetForegroundWindow(new WindowInteropHelper(_menuOwner).Handle);
        menu.IsOpen = true;
    }

    void ShowSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized)
            _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void Dispose()
    {
        Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyWindow.Dispose();
        _watcher.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _settings?.Close();
        _menuOwner.Close();
    }
}
