using Shisui.Core.Interfaces;

namespace Shisui.Core.Services;

/// <summary>ネットワーク設定変更を直列化するプロセス内共有ゲート。</summary>
public sealed class NetworkMutationGate : INetworkMutationGate, IDisposable
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        await semaphore.WaitAsync(ct);
        return new Lease(semaphore);
    }

    public void Dispose() => semaphore.Dispose();

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? owner = semaphore;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}
