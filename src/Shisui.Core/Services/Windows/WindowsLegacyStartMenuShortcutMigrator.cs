using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// v1.0.7 までに作成された著者名サブフォルダ内のスタートメニューショートカットを、
/// 現行のスタートメニュー直下へ一度だけ移行する。
/// </summary>
public static partial class WindowsLegacyStartMenuShortcutMigrator
{
    private const int ShellChangeNotifyUpdateDirectory = 0x00001000;
    private const uint ShellChangeNotifyPathUnicode = 0x0005;
    private const uint ShellChangeNotifyFlushNoWait = 0x3000;
    private const uint ShellChangeNotifyRecursive = 0x10000;
    private const string LegacyFolderName = "ゆろち";
    private const string ShortcutFileName = "Shisui.lnk";

    /// <summary>
    /// 現在のユーザーのスタートメニューで旧ショートカットの移行を試みる。
    /// 更新フックを失敗させないよう、ファイルシステム上の競合は無視する。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void MigrateForCurrentUser()
    {
        _ = TryMigrateAndRefresh(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            RefreshShellStartMenu);
    }

    /// <summary>
    /// 旧ショートカットを移行し、移行済みの場合も Start メニューの古い表示キャッシュを更新する。
    /// </summary>
    internal static bool TryMigrateAndRefresh(string programsDirectory, Action<string> refreshShell)
    {
        ArgumentNullException.ThrowIfNull(refreshShell);

        var migrated = TryMigrate(programsDirectory);
        if (!string.IsNullOrWhiteSpace(programsDirectory) && Directory.Exists(programsDirectory))
        {
            refreshShell(programsDirectory);
        }
        return migrated;
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

    [SupportedOSPlatform("windows")]
    internal static void RefreshShellStartMenu(string programsDirectory)
    {
        if (string.IsNullOrWhiteSpace(programsDirectory) || !Directory.Exists(programsDirectory))
        {
            return;
        }

        var pathPointer = Marshal.StringToHGlobalUni(programsDirectory);
        try
        {
            SHChangeNotify(
                ShellChangeNotifyUpdateDirectory,
                ShellChangeNotifyPathUnicode | ShellChangeNotifyFlushNoWait | ShellChangeNotifyRecursive,
                pathPointer,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHChangeNotify")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
