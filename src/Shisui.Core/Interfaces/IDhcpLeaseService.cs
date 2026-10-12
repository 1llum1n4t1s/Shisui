using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>選択したアダプターの DHCP リースだけを解放し、再取得と回復確認を行う。</summary>
public interface IDhcpLeaseService
{
    Task<IReadOnlyList<CommandExecutionResult>> ReacquireAsync(string adapterId, CancellationToken ct = default);
}
