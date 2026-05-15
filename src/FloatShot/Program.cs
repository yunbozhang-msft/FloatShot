using System.Threading;
using System.Windows.Forms;

namespace FloatShot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 单实例: 用命名 Mutex 防止重复启动
        using var mutex = new Mutex(true, "FloatShot.SingleInstance.Mutex", out var createdNew);
        if (!createdNew)
        {
            // 已在运行: 给已有实例发个广播，让它显示按钮 + 弹气泡
            AppMessages.NotifyExisting();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        using var ctx = new TrayContext();
        Application.Run(ctx);
    }
}
