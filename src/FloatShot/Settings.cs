using System.Text.Json;
using System.Text.Json.Serialization;

namespace FloatShot;

internal sealed class Settings
{
    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public string FileNamePattern { get; set; } = "shot_{0:yyyyMMdd_HHmmss_fff}.png";

    public CaptureMode DefaultMode { get; set; } = CaptureMode.Region;

    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+S";

    public string RegionHotkey { get; set; } = "Ctrl+Alt+Shift+R";

    public string FullScreenHotkey { get; set; } = "Ctrl+Alt+Shift+F";

    public bool ShowFloatingButton { get; set; } = true;

    public int ButtonX { get; set; } = -1;  // -1 = use default (right-bottom)

    public int ButtonY { get; set; } = -1;

    public bool CopyToClipboard { get; set; } = true;

    public bool OpenFolderAfterCapture { get; set; } = false;

    public bool RunAtStartup { get; set; } = false;

    [JsonIgnore]
    public string FilePath => GetFilePath();

    private static string GetFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FloatShot");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static Settings Load()
    {
        var path = GetFilePath();
        if (!File.Exists(path))
            return new Settings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(GetFilePath(), json);
        }
        catch { /* swallow */ }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

internal enum CaptureMode
{
    Region,
    FullScreen,
    PrimaryScreen,
    ActiveWindow
}
