using System.Drawing;

namespace FloatShot;

/// <summary>
/// Segoe Fluent Icons (Win11) / Segoe MDL2 Assets (Win10) glyph 常量与字体辅助。
/// 完整 glyph 表: https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
/// </summary>
internal static class Icons
{
    public const string FontFamily   = "Segoe Fluent Icons";
    public const string FallbackFont = "Segoe MDL2 Assets";

    // Glyphs (Unicode PUA)
    public const string Camera   = "\uE722";   // Camera
    public const string Pin      = "\uE718";   // Pin
    public const string Copy     = "\uE8C8";   // Copy
    public const string Save     = "\uE74E";   // Save
    public const string Cancel   = "\uE711";   // Cancel
    public const string Close    = "\uE10A";   // Close (small)
    public const string Settings = "\uE713";   // Settings
    public const string Folder   = "\uE8B7";   // FolderOpen
    public const string ZoomIn   = "\uE8A3";   // ZoomIn
    public const string ZoomOut  = "\uE71F";   // ZoomOut

    private static FontFamily? _ff;

    /// <summary>取一个最佳可用的 Fluent Icons FontFamily, 自动 fallback。</summary>
    public static FontFamily GetFamily()
    {
        if (_ff is not null) return _ff;
        try { _ff = new FontFamily(FontFamily); return _ff; }
        catch
        {
            try { _ff = new FontFamily(FallbackFont); return _ff; }
            catch { _ff = System.Drawing.FontFamily.GenericSansSerif; return _ff; }
        }
    }

    public static Font GetFont(float size, FontStyle style = FontStyle.Regular)
        => new(GetFamily(), size, style, GraphicsUnit.Pixel);
}
