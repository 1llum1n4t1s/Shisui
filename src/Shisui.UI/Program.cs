using Avalonia;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;
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
        var velopackApp = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            // v1.0.7 までの StartMenu は「ゆろち」サブフォルダにショートカットを置いていた。
            // 更新フックは Velopack 自身のショートカット再計算より前に走るため、先に直下へ移す。
            velopackApp.OnAfterUpdateFastCallback(MigrateLegacyStartMenuShortcut);
        }

        velopackApp.Run();

        // 更新フックが一時的なファイルロックで移行できなかった場合も、通常起動時に再試行する。
        // 管理者権限を要求する前に実行するため、ユーザーのスタートメニューだけを安全に操作できる。
        if (OperatingSystem.IsWindows())
        {
            WindowsLegacyStartMenuShortcutMigrator.MigrateForCurrentUser();
        }

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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void MigrateLegacyStartMenuShortcut(SemanticVersion _)
    {
        WindowsLegacyStartMenuShortcutMigrator.MigrateForCurrentUser();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
