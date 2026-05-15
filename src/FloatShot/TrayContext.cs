using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace FloatShot;

internal sealed class TrayContext : ApplicationContext
{
    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeys;
    private readonly System.Windows.Forms.Timer _topMostTimer;
    private readonly MessageWindow _msgWindow;
    private FloatingButton? _button;

    public TrayContext()
    {
        _settings = Settings.Load();

        _tray = new NotifyIcon
        {
            Icon    = AppIcon.Get(),
            Text    = "FloatShot",
            Visible = true
        };
        _tray.ContextMenuStrip = BuildTrayMenu();
        _tray.DoubleClick += (_, _) => DoCapture(_settings.DefaultMode);

        _hotkeys = new HotkeyWindow();
        RegisterHotkeys();

        if (_settings.ShowFloatingButton)
            ShowButton();

        _topMostTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (_button is { Visible: true, IsHandleCreated: true })
                WindowOps.EnsureTopMost(_button.Handle);
        };
        _topMostTimer.Start();

        _msgWindow = new MessageWindow(() => ShowOrCreateButton());

        ShowBalloon("FloatShot is running",
            $"Hotkey: {_settings.Hotkey}  |  Right-click tray for options",
            ToolTipIcon.Info);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        AddMenuItem(menu, "Region capture",     (_, _) => DoCapture(CaptureMode.Region));
        AddMenuItem(menu, "Full screen",        (_, _) => DoCapture(CaptureMode.FullScreen));
        AddMenuItem(menu, "Primary screen",     (_, _) => DoCapture(CaptureMode.PrimaryScreen));
        AddMenuItem(menu, "Active window",      (_, _) => DoCapture(CaptureMode.ActiveWindow));
        menu.Items.Add(new ToolStripSeparator());

        var miButton = new ToolStripMenuItem("Show floating button") { CheckOnClick = true, Checked = _settings.ShowFloatingButton };
        miButton.Click += (_, _) =>
        {
            _settings.ShowFloatingButton = miButton.Checked;
            _settings.Save();
            if (miButton.Checked) ShowOrCreateButton();
            else HideButton();
        };
        menu.Items.Add(miButton);

        AddMenuItem(menu, "Open save folder",   (_, _) => OpenFolder());
        AddMenuItem(menu, "Settings...",        (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        AddMenuItem(menu, "About FloatShot",    (_, _) => ShowAbout());
        AddMenuItem(menu, "Exit",               (_, _) => ExitThread());
        return menu;
    }

    private static void AddMenuItem(ContextMenuStrip menu, string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += onClick;
        menu.Items.Add(item);
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        TryHotkey(_settings.Hotkey,           () => DoCapture(_settings.DefaultMode));
        TryHotkey(_settings.RegionHotkey,     () => DoCapture(CaptureMode.Region));
        TryHotkey(_settings.FullScreenHotkey, () => DoCapture(CaptureMode.FullScreen));
    }

    private void TryHotkey(string spec, Action action)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        if (!_hotkeys.TryRegister(spec, action, out var err))
        {
            ShowBalloon("Hotkey not registered", $"{spec}: {err}", ToolTipIcon.Warning);
        }
    }

    private void ShowOrCreateButton()
    {
        if (_button is null || _button.IsDisposed) ShowButton();
        else if (!_button.Visible) _button.Visible = true;
        if (_button is not null) WindowOps.EnsureTopMost(_button.Handle);
    }

    private void ShowButton()
    {
        _button?.Dispose();
        _button = new FloatingButton();
        _button.CaptureClicked += (_, _) => DoCapture(_settings.DefaultMode);
        _button.RightClicked   += (_, _) => ShowButtonMenu();
        _button.Move           += (_, _) => SaveButtonPosition();

        // 初始位置: 已有保存的就用, 否则放主屏右下偏上
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var defaultLoc = new Point(wa.Right - 80, wa.Bottom - 200);
        var loc = (_settings.ButtonX < 0 || _settings.ButtonY < 0)
            ? defaultLoc
            : new Point(_settings.ButtonX, _settings.ButtonY);
        // 防止上次保存的位置已不在任何屏幕上
        var screens = Screen.AllScreens;
        if (!screens.Any(s => s.Bounds.Contains(loc))) loc = defaultLoc;
        _button.Location = loc;

        _button.Show();
    }

    private void HideButton()
    {
        if (_button is not null) _button.Visible = false;
    }

    private void SaveButtonPosition()
    {
        if (_button is null) return;
        _settings.ButtonX = _button.Location.X;
        _settings.ButtonY = _button.Location.Y;
        _settings.Save();
    }

    private void ShowButtonMenu()
    {
        var menu = BuildTrayMenu();
        menu.Show(Cursor.Position);
    }

    private void DoCapture(CaptureMode mode)
    {
        try
        {
            var path = Capture.Run(mode, _settings, _button);
            if (path is null)
            {
                // Region 模式选了 Pin/Copy/Cancel, 或非 Region 模式被取消 - 不弹气泡
                return;
            }
            ShowBalloon("Screenshot saved", path, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowBalloon("Capture failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
            Process.Start("explorer.exe", _settings.SaveFolder);
        }
        catch (Exception ex)
        {
            ShowBalloon("Open folder failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            if (dlg.RestartHotkeysRequired) RegisterHotkeys();
            if (dlg.RestartButtonRequired)
            {
                if (_settings.ShowFloatingButton) ShowOrCreateButton();
                else HideButton();
            }
        }
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? Application.ProductVersion;

        MessageBox.Show(
            $"FloatShot {version}\n\n" +
            "Screenshot helper for Windows App + Dev Box / Cloud PC.\n\n" +
            "Use it when Teams runs inside the remote desktop but\n" +
            "in-session screenshots show blank, black, or white meeting\n" +
            "content because Teams VDI media is rendered locally.\n\n" +
            "FloatShot captures the local screen. It does not bypass\n" +
            "screen capture protection or security policy.\n\n" +
            "MIT License",
            "About FloatShot",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText  = text;
        _tray.BalloonTipIcon  = icon;
        _tray.ShowBalloonTip(2000);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _topMostTimer?.Dispose();
            _hotkeys?.Dispose();
            _button?.Dispose();
            _msgWindow?.Dispose();
            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    /// <summary>接收 AppMessages.ShowMessage 的隐藏窗口</summary>
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action _onShow;
        public MessageWindow(Action onShow)
        {
            _onShow = onShow;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == AppMessages.ShowMessage)
            {
                try { _onShow(); } catch { }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { DestroyHandle(); } catch { }
        }
    }
}
