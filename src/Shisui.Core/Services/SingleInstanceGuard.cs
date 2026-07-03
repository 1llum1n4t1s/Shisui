namespace Shisui.Core.Services;

/// <summary>
/// 多重起動防止。名前付き Mutex は macOS では挙動が不安定なため、
/// OS 非依存のファイルロック (FileShare.None) で実装する。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly FileStream? _lockStream;

    public SingleInstanceGuard(string appName)
    {
        var dir = Path.Combine(Path.GetTempPath(), appName);
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "instance.lock");

        try
        {
            _lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            _lockStream = null;
        }
    }

    public bool TryAcquire() => _lockStream is not null;

    public void Dispose() => _lockStream?.Dispose();
}
