using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

public interface IDnsCacheService
{
    Task<CommandExecutionResult> FlushAsync(CancellationToken ct = default);
}
