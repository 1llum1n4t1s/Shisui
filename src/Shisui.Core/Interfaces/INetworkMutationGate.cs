namespace Shisui.Core.Interfaces;

/// <summary>一時的なネットワーク設定変更をプロセス内で直列化する。</summary>
public interface INetworkMutationGate
{
    /// <summary>排他権を取得し、破棄時に解放するリースを返す。</summary>
    Task<IDisposable> EnterAsync(CancellationToken ct = default);
}
