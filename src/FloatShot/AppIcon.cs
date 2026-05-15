using System.Drawing;
using System.Reflection;

namespace FloatShot;

internal static class AppIcon
{
    private static Icon? _cached;

    public static Icon Get()
    {
        if (_cached is not null) return _cached;
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("floatshot.ico", StringComparison.OrdinalIgnoreCase));
        if (resName is not null)
        {
            using var s = asm.GetManifestResourceStream(resName);
            if (s is not null)
            {
                _cached = new Icon(s);
                return _cached;
            }
        }
        _cached = SystemIcons.Application;
        return _cached;
    }
}
