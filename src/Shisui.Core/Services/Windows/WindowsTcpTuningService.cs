using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsTcpTuningService(ICommandExecutor executor) : ITcpTuningService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsTcpCommandBuilder.BuildEnableBbr2())
        {
            results.Add(await executor.RunAsync(WindowsTcpCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> RevertBbr2ToDefaultAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsTcpCommandBuilder.BuildRevertBbr2ToDefault())
        {
            results.Add(await executor.RunAsync(WindowsTcpCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildSetGlobalOption(option, enabled), ct);

    public Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.ShowGlobalStatus, ct);

    public async Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsTcpStateCommandBuilder.FileName, WindowsTcpStateCommandBuilder.Arguments, ct);
        return result.Success ? WindowsTcpStateParser.Parse(result.StandardOutput) : TcpSettingsSnapshot.Unknown;
    }
}
