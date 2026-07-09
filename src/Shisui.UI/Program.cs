using Avalonia;
using Shisui.Core.Services;
using Shisui.UI.Services;
using Velopack;

namespace Shisui.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack のブートストラップを最初に走らせる
        // (--veloapp-install / --veloapp-updated 等の internal hook を捌くため、Avalonia 起動・多重起動ガードより前に必須)
        VelopackApp.Build().Run();

        // app.manifest は asInvoker。ここで自己再起動して昇格する (詳細は app.manifest のコメント参照)。
        // SingleInstanceGuard より前に行う: 非昇格プロセスがロックを握ったまま昇格版を起動すると
        // 昇格版が二重起動判定で弾かれてしまうため。
        // デバッガアタッチ中はスキップする: 自己再起動すると、デバッガが付いている現プロセスが
        // 終了して昇格版が別プロセスとして立ち上がるため、デバッグセッションがそこで切れてしまう。
        // 非昇格のまま続行し、管理者権限が要る操作 (netsh 等) は個別に失敗させて動作確認する。
        if (OperatingSystem.IsWindows()
            && !System.Diagnostics.Debugger.IsAttached
            && !WindowsElevationHelper.IsRunningAsAdministrator())
        {
            return WindowsElevationHelper.TryRelaunchElevated(args) ? 0 : 1;
        }

        using var singleInstance = new SingleInstanceGuard("Shisui");
        if (!singleInstance.TryAcquire())
        {
            return 1;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
