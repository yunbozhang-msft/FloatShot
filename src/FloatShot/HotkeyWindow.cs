using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FloatShot;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    private const int WM_HOTKEY = 0x0312;

    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>注册一个热键, 失败返回 false。spec 形如 "Ctrl+Alt+Shift+S"。</summary>
    public bool TryRegister(string spec, Action onPress, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "empty hotkey";
            return false;
        }
        if (!ParseSpec(spec, out var mods, out var vk, out error))
            return false;

        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, vk))
        {
            error = $"RegisterHotKey failed for '{spec}' (already in use?)";
            return false;
        }
        _handlers[id] = onPress;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList())
        {
            try { UnregisterHotKey(Handle, id); } catch { }
        }
        _handlers.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (_handlers.TryGetValue(id, out var h))
            {
                try { h(); } catch { }
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        try { DestroyHandle(); } catch { }
    }

    /// <summary>解析 "Ctrl+Alt+Shift+S" 这种字符串。</summary>
    public static bool ParseSpec(string spec, out uint mods, out uint vk, out string? error)
    {
        mods = 0; vk = 0; error = null;
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { error = "empty"; return false; }
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; break;
                case "alt":                  mods |= MOD_ALT;     break;
                case "shift":                mods |= MOD_SHIFT;   break;
                case "win": case "windows":  mods |= MOD_WIN;     break;
                default: error = $"unknown modifier '{parts[i]}'"; return false;
            }
        }
        var key = parts[^1];
        if (Enum.TryParse<Keys>(key, ignoreCase: true, out var k))
        {
            vk = (uint)k;
            return true;
        }
        if (key.Length == 1)
        {
            vk = char.ToUpperInvariant(key[0]);
            return true;
        }
        error = $"unknown key '{key}'";
        return false;
    }
}
