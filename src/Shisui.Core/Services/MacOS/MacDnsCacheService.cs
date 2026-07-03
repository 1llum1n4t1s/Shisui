using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacDnsCacheService(ICommandExecutor executor) : IDnsCacheService
{
    public async Task<CommandExecutionResult> FlushAsync(CancellationToken ct = default)
    {
        var flush = await executor.RunAsync("dscacheutil", "-flushcache", ct);
        var restart = await executor.RunAsync("killall", "-HUP mDNSResponder", ct);

        return new CommandExecutionResult(
            flush.Success && restart.Success,
            $"{flush.CommandLine} && {restart.CommandLine}",
            restart.ExitCode,
            string.Join('\n', new[] { flush.StandardOutput, restart.StandardOutput }.Where(s => s.Length > 0)),
            string.Join('\n', new[] { flush.StandardError, restart.StandardError }.Where(s => s.Length > 0)));
    }
}
