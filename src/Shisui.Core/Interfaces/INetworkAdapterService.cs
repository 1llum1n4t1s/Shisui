using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

public interface INetworkAdapterService
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default);
}
