using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>PnP 上で切断済みのネットワークデバイス登録を全削除し、対象名が指定された場合だけ連番を外す。</summary>
public interface INetworkAdapterNameService
{
    Task<NetworkAdapterNameCleanupResult> CleanupAsync(
        string? currentName,
        CancellationToken ct = default);
}
