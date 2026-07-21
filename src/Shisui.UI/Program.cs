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
        // 製品版では Velopack がショートカットへ設定する AUMID と実行プロセスを一致させる。
        // Debug版まで製品版のAUMIDを名乗ると、Windowsがインストール済みショートカットの
        // アイコン情報を参照し、開発用EXEのタスクバーアイコンが白紙になるため設定しない。
#if !DEBUG
        if (OperatingSystem.IsWindows())
        {
            WindowsElevationHelper.TrySetCurrentProcessAppUserModelId();
        }
#endif

        // Velopack のブートストラップは Avalonia 起動・昇格・多重起動ガードより前に走らせる。
        // (--veloapp-install / --veloapp-updated 等の internal hook を捌くために必須)
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

            // 旧PerUser版は管理者実行するバイナリがユーザー書き込み可能なため、通常起動より先に
            // 署名済みPerMachine MSIへ移行する。Velopackの内部フックは上のRun()で既に処理済み。
            var migrationExitCode = WindowsPerMachineMigration.HandleStartupAsync(args).GetAwaiter().GetResult();
            if (migrationExitCode is not null)
            {
                return migrationExitCode.Value;
            }
        }

        // app.manifest は asInvoker。ここで自己再起動して昇格する (詳細は app.manifest のコメント参照)。
        // SingleInstanceGuard より前に行う: 非昇格プロセスがロックを握ったまま昇格版を起動すると
        // 昇格版が二重起動判定で弾かれてしまうため。
        // Debug版も設定変更を伴う計測を実行するため昇格する。デバッガを接続したまま確認する場合は、
        // Visual Studio自体を管理者として起動して最初から昇格済みのプロセスとして開始する。
        if (OperatingSystem.IsWindows()
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
