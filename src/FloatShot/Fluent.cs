using System.Drawing;

namespace FloatShot;

/// <summary>
/// Win11 Fluent Design 设计令牌 + Segoe Fluent Icons 字符常量。
/// 参考: https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
/// </summary>
internal static class Fluent
{
    // ===== 颜色 (Win11 dark theme) =====
    public static readonly Color Bg            = Color.FromArgb(255, 32,  32,  32);   // #202020
    public static readonly Color BgElevated    = Color.FromArgb(255, 43,  43,  43);   // 控件背景
    public static readonly Color BgHover       = Color.FromArgb(255, 60,  60,  60);
    public static readonly Color BgPressed     = Color.FromArgb(255, 75,  75,  75);
    public static readonly Color Border        = Color.FromArgb(255, 58,  58,  58);   // #3A3A3A
    public static readonly Color BorderSubtle  = Color.FromArgb(120, 255, 255, 255);
    public static readonly Color Text          = Color.FromArgb(255, 255, 255, 255);
    public static readonly Color TextSecondary = Color.FromArgb(180, 255, 255, 255);

    public static readonly Color Accent        = Color.FromArgb(255, 0,   120, 212);  // #0078D4
    public static readonly Color AccentHover   = Color.FromArgb(255, 38,  148, 234);

    public static readonly Color AccentBlue    = Color.FromArgb(255, 0,   120, 212);
    public static readonly Color AccentPurple  = Color.FromArgb(255, 135, 100, 184);
    public static readonly Color AccentGreen   = Color.FromArgb(255, 16,  137, 62);
    public static readonly Color AccentRed     = Color.FromArgb(255, 196, 43,  28);

    // ===== 几何 =====
    public const int Radius         = 6;
    public const int RadiusSmall    = 4;
    public const int Padding        = 8;
    public const int PaddingSmall   = 4;

    // ===== 字体 =====
    public const string IconFontName = "Segoe Fluent Icons";
    public const string IconFontFallback = "Segoe MDL2 Assets";
    public const string TextFontName = "Segoe UI Variable";
    public const string TextFontFallback = "Segoe UI";

    public static Font IconFont(float size) => SafeFont(IconFontName, IconFontFallback, size, FontStyle.Regular);
    public static Font TextFont(float size, FontStyle style = FontStyle.Regular) => SafeFont(TextFontName, TextFontFallback, size, style);

    private static Font SafeFont(string primary, string fallback, float size, FontStyle style)
    {
        try
        {
            var f = new Font(primary, size, style, GraphicsUnit.Point);
            if (string.Equals(f.FontFamily.Name, primary, StringComparison.OrdinalIgnoreCase))
                return f;
            f.Dispose();
        }
        catch { }
        return new Font(fallback, size, style, GraphicsUnit.Point);
    }

    // ===== Segoe Fluent Icons 字符 (Unicode PUA) =====
    // 完整列表: https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
    public static class Icons
    {
        public const string Camera     = "\uE722";  // 相机 (Camera)
        public const string Crop       = "\uE7A8";  // 裁剪 (Crop)
        public const string Pin        = "\uE718";  // 图钉 (Pinned)
        public const string Copy       = "\uE8C8";  // 复制 (Copy)
        public const string Save       = "\uE74E";  // 保存 (Save)
        public const string Cancel     = "\uE711";  // 关闭 (Cancel)
        public const string Settings   = "\uE713";  // 设置 (Settings)
        public const string Folder     = "\uE8B7";  // 文件夹 (FolderOpen)
        public const string FullScreen = "\uE740";  // 全屏 (FullScreen)
        public const string Window     = "\uE737";  // 单窗口 (TVMonitor)
        public const string ZoomIn     = "\uE8A3";  // 放大
        public const string ZoomOut    = "\uE71F";  // 缩小
        public const string Refresh    = "\uE72C";  // 重置/刷新
        public const string Info       = "\uE946";
        public const string Exit       = "\uE7E8";
    }
}
