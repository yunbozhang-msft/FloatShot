using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Runs the production menu wiring and modal Settings form on an STA message loop.
// The fixture omits TrayContext's startup side effects (global hotkeys, tray icon,
// loading/saving user settings). It does not replace the menu or dialog handlers.
internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly AppAssembly = Assembly.LoadFrom(
        Environment.GetEnvironmentVariable("FLOATSHOT_UI_TEST_ASSEMBLY")
        ?? Path.Combine(AppContext.BaseDirectory, "FloatShot.dll"));
    private static readonly Type ContextType = AppAssembly.GetType("FloatShot.TrayContext", true)!;
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        using var watchdog = new System.Threading.Timer(_ =>
        {
            Console.Error.WriteLine("FAIL: UI test exceeded 90 seconds (possible blocked modal loop)");
            Environment.Exit(2);
        }, null, args.Contains("--interactive") ? Timeout.Infinite : 90_000, Timeout.Infinite);
        using var host = new Form
        {
            Text = "FloatShot UI regression test",
            ClientSize = new Size(540, 160),
            StartPosition = FormStartPosition.CenterScreen
        };
        using var fixture = new Fixture();
        if (args.Contains("--interactive"))
        {
            foreach (bool ephemeral in new[] { false, true })
            {
                var button = new Button
                {
                    Text = ephemeral ? "Floating button menu" : "Tray menu",
                    Bounds = new Rectangle(ephemeral ? 280 : 20, 30, 240, 50)
                };
                button.Click += (_, _) => fixture.ShowMenu(host, ephemeral);
                host.Controls.Add(button);
            }
        }
        else
        {
            host.Shown += async (_, _) =>
            {
                foreach (bool ephemeral in new[] { false, true })
                foreach (bool keyboard in new[] { false, true })
                {
                    string name = $"{(ephemeral ? "floating/disposed" : "tray/reused")} / {(keyboard ? "keyboard" : "mouse")}";
                    try
                    {
                        using var caseFixture = new Fixture();
                        await RunCase(host, caseFixture, ephemeral, keyboard);
                        Console.WriteLine($"PASS {name}");
                    }
                    catch (Exception ex)
                    {
                        _failures++;
                        Console.WriteLine($"FAIL {name}: {ex.Message}");
                        foreach (Form dialog in Application.OpenForms.Cast<Form>().Where(f => f != host).ToArray())
                            dialog.Close();
                    }
                }
                Console.WriteLine($"RESULT: {_failures} failure(s)");
                host.Close();
            };
        }
        Application.Run(host);
        return _failures == 0 ? 0 : 1;
    }

    private static async Task RunCase(Form host, Fixture fixture, bool ephemeral, bool keyboard)
    {
        // Opening twice catches stale menu flags and reusing/disposal of popup menus.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var events = new List<string>();
            var menu = fixture.ShowMenu(host, ephemeral);
            var item = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "Settings...");
            ToolStripDropDownClosedEventHandler closed = (_, _) => events.Add("Closed");
            EventHandler clicked = (_, _) => events.Add("Click");
            menu.Closed += closed;
            item.Click += clicked;
            item.Select();
            await Task.Delay(80);
            if (keyboard)
            {
                // WinForms' real keyboard dispatch, including menu dismissal.
                typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", PrivateInstance)!
                    .Invoke(menu, new object[] { Keys.Enter });
            }
            else
            {
                var bounds = item.Bounds;
                int point = ((bounds.Top + bounds.Height / 2) << 16) | (bounds.Left + bounds.Width / 2);
                SendMessage(menu.Handle, 0x0201, 1, point); // WM_LBUTTONDOWN
                SendMessage(menu.Handle, 0x0202, 0, point); // WM_LBUTTONUP
            }
            await WaitUntil(() => FindSettings() is not null, 2000,
                $"Settings did not open; events: {string.Join(" -> ", events)}");
            var dialog = FindSettings()!;
            try
            {
                Check(!menu.Visible, "Context menu must be closed before Settings opens");
                if (ephemeral) Check(menu.IsDisposed, "Floating menu must be disposed before Settings opens");
                Check(dialog.Modal, "Settings must be a modal dialog");
                Check(!fixture.TopMostTimer.Enabled, "Topmost refresh must pause while Settings is open");
                var combo = dialog.Controls.OfType<ComboBox>().Single();
                Check(combo.Items.Count == 4, "Default mode must contain four choices");
                for (int mode = 0; mode < 4; mode++)
                {
                    combo.Focus();
                    // Exercise the native ComboBox arrow's mouse handling, not DroppedDown=true.
                    int point = ((combo.ClientSize.Height / 2) << 16) | (combo.ClientSize.Width - 8);
                    SendMessage(combo.Handle, 0x0201, 1, point);
                    SendMessage(combo.Handle, 0x0202, 0, point);
                    await Task.Delay(650); // exceeds the production topmost timer interval
                    var info = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
                    Check(GetComboBoxInfo(combo.Handle, ref info), "Cannot query native ComboBox popup");
                    Check(combo.DroppedDown && IsWindowVisible(info.List), "Default mode popup did not stay visible");
                    // Select using the native keyboard path; verify the displayed result.
                    SendMessage(combo.Handle, 0x0100, 0x24, 0); // Home
                    for (int i = 0; i < mode; i++) SendMessage(combo.Handle, 0x0100, 0x28, 0); // Down
                    SendMessage(combo.Handle, 0x0100, 0x0D, 0); // Enter
                    Check(combo.SelectedIndex == mode && !combo.DroppedDown,
                        $"Cannot select capture mode {mode}");
                }
                dialog.Controls.OfType<Button>().Single(b => b.Text == "Cancel").PerformClick();
                await WaitUntil(() => FindSettings() is null, 1000, "Cancel did not close Settings");
                Check(fixture.TopMostTimer.Enabled, "Topmost refresh did not resume after Settings closed");
                Check(Convert.ToInt32(fixture.Settings.GetType().GetProperty("DefaultMode")!.GetValue(fixture.Settings)) == 0,
                    "Cancel changed the configured capture mode");
                Console.WriteLine($"  attempt {attempt + 1}: {string.Join(" -> ", events)}; four modes selectable; Cancel preserved settings");
            }
            finally
            {
                if (!dialog.IsDisposed) dialog.Close();
                if (!menu.IsDisposed) { menu.Closed -= closed; item.Click -= clicked; }
            }
        }
        var dismissedMenu = fixture.ShowMenu(host, ephemeral);
        dismissedMenu.Close(ToolStripDropDownCloseReason.Keyboard);
        await Task.Delay(100);
        Check(FindSettings() is null, "Dismissing the menu unexpectedly opened Settings");
    }

    private static Form? FindSettings() => Application.OpenForms.Cast<Form>()
        .SingleOrDefault(f => f.GetType().FullName == "FloatShot.SettingsForm");

    private static async Task WaitUntil(Func<bool> predicate, int timeoutMs, string message)
    {
        var timeout = Environment.TickCount64 + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount64 >= timeout) throw new InvalidOperationException(message);
            await Task.Delay(30);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ApplicationContext _context = (ApplicationContext)RuntimeHelpers.GetUninitializedObject(ContextType);
        private readonly Control _dispatcher = new();
        private ContextMenuStrip? _trayMenu;
        public readonly System.Windows.Forms.Timer TopMostTimer = new() { Interval = 500 };
        public readonly object Settings = Activator.CreateInstance(AppAssembly.GetType("FloatShot.Settings", true)!, true)!;

        public Fixture()
        {
            _dispatcher.CreateControl();
            SetField("_settings", Settings);
            SetField("_uiDispatcher", _dispatcher);
            SetField("_topMostTimer", TopMostTimer);
            TopMostTimer.Start();
        }

        private void SetField(string name, object value) => ContextType.GetField(name, PrivateInstance)!.SetValue(_context, value);

        public ContextMenuStrip ShowMenu(Form host, bool ephemeral)
        {
            ContextMenuStrip Build() => (ContextMenuStrip)ContextType.GetMethod("BuildTrayMenu", PrivateInstance)!
                .Invoke(_context, new object[] { ephemeral })!;
            var menu = ephemeral ? Build() : _trayMenu ??= Build();
            if (ephemeral) menu.Closed += (_, _) => _dispatcher.BeginInvoke((Action)menu.Dispose);
            menu.Show(host, new Point(25, 95));
            return menu;
        }

        public void Dispose()
        {
            _trayMenu?.Dispose();
            _context.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public Rect Item, Button;
        public uint ButtonState;
        public nint Combo, Edit, List;
    }
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(nint window, ref ComboBoxInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
}
