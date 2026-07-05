using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

public interface INetworkAdapterService
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default);

    /// <summary>指定アダプタの読み取り専用詳細情報 (リンク速度・MAC アドレス等) を取得する。取得できない場合は null。</summary>
    Task<NetworkAdapterDetails?> GetAdapterDetailsAsync(string adapterId, CancellationToken ct = default);
}
