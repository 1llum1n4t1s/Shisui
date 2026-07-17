using System.Runtime.Versioning;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// v1.0.7 までに作成された著者名サブフォルダ内のスタートメニューショートカットを、
/// 現行のスタートメニュー直下へ一度だけ移行する。
/// </summary>
public static class WindowsLegacyStartMenuShortcutMigrator
{
    private const string LegacyFolderName = "ゆろち";
    private const string ShortcutFileName = "Shisui.lnk";

    /// <summary>
    /// 現在のユーザーのスタートメニューで旧ショートカットの移行を試みる。
    /// 更新フックを失敗させないよう、ファイルシステム上の競合は無視する。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void MigrateForCurrentUser()
    {
        _ = TryMigrate(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
    }

    /// <summary>
    /// 指定したスタートメニュー Programs ディレクトリで旧ショートカットを移行する。
    /// 既に直下のショートカットがある場合は、利用者のリンクを上書きしない。
    /// </summary>
    internal static bool TryMigrate(string programsDirectory)
    {
        if (string.IsNullOrWhiteSpace(programsDirectory))
        {
            return false;
        }

        var legacyDirectory = Path.Combine(programsDirectory, LegacyFolderName);
        var legacyShortcutPath = Path.Combine(legacyDirectory, ShortcutFileName);
        var rootShortcutPath = Path.Combine(programsDirectory, ShortcutFileName);

        if (!File.Exists(legacyShortcutPath) || File.Exists(rootShortcutPath))
        {
            return false;
        }

        try
        {
            File.Move(legacyShortcutPath, rootShortcutPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        TryDeleteIfEmpty(legacyDirectory);
        return true;
    }

    private static void TryDeleteIfEmpty(string directoryPath)
    {
        try
        {
            // recursive にはせず、他のショートカットや利用者のファイルは必ず残す。
            Directory.Delete(directoryPath);
        }
        catch (IOException)
        {
            // ディレクトリが空でないか、一時的に使用中ならそのまま残す。
        }
        catch (UnauthorizedAccessException)
        {
            // 更新処理を妨げない。
        }
    }
}
