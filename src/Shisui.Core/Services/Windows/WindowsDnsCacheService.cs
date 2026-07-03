using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDnsCacheService(ICommandExecutor executor) : IDnsCacheService
{
    public Task<CommandExecutionResult> FlushAsync(CancellationToken ct = default) =>
        executor.RunAsync("ipconfig", "/flushdns", ct);
}
