namespace Shisui.Core.Services;

/// <summary>旧ログを設定とは別に移し、移せなかったファイルは次回に残す。</summary>
internal static class LegacyLogMigration
{
    private const string MutexName = @"Local\Shisui.LegacyLogMigration";

    internal static void TryMigrate(string source, string destination, string mutexName = MutexName, bool removeEmptyParent = false)
    {
        try
        {
            using var mutex = new Mutex(false, mutexName);
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired || HasReparsePoint(source) || HasReparsePoint(destination))
                    return;

                if (!Directory.Exists(source))
                {
                    if (removeEmptyParent) TryDeleteEmptyDirectory(Path.GetDirectoryName(source)!);
                    return;
                }

                Directory.CreateDirectory(destination);
                if (HasReparsePoint(destination)) return;

                foreach (var file in Directory.EnumerateFiles(source, "*.log", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        // 書込み中の旧プロセスを拒否し、移動が終わるまで新たな書込みを防ぐ。
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                        var target = Path.Combine(destination, Path.GetFileName(file));
                        if (File.Exists(target))
                            target = Path.Combine(destination, $"{Path.GetFileNameWithoutExtension(file)}-{Guid.NewGuid():N}.log");
                        File.Move(file, target);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                // 他のファイルや処理中のログが残っていれば削除されない。
                TryDeleteEmptyDirectory(source);
                if (removeEmptyParent) TryDeleteEmptyDirectory(Path.GetDirectoryName(source)!);
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { Directory.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool HasReparsePoint(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
        }
        return false;
    }
}
