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

    public async Task<IReadOnlyList<CommandExecutionResult>> SetCongestionProvidersAsync(
        IReadOnlyDictionary<string, string> providers,
        CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsTcpCommandBuilder.BuildSetCongestionProviders(providers))
        {
            results.Add(await executor.RunAsync(WindowsTcpCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public Task<CommandExecutionResult> ResetAllTcpSettingsToDefaultAsync(CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.ResetAllToDefault, ct);

    public async Task<IReadOnlyList<CommandExecutionResult>> RevertGlobalOptionsToDefaultAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsTcpCommandBuilder.BuildRevertGlobalOptionsToDefault())
        {
            results.Add(await executor.RunAsync(WindowsTcpCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public Task<CommandExecutionResult> RevertLegacyTcpRegistryTweaksToDefaultAsync(CancellationToken ct = default) =>
        executor.RunAsync(
            WindowsLegacyTcpRegistryCommandBuilder.FileName,
            WindowsLegacyTcpRegistryCommandBuilder.Arguments,
            ct);

    public Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildSetGlobalOption(option, enabled), ct);

    public Task<CommandExecutionResult> RevertTcpGlobalOptionToDefaultAsync(
        TcpGlobalOption option,
        CancellationToken ct = default) =>
        executor.RunAsync(
            WindowsTcpCommandBuilder.FileName,
            WindowsTcpCommandBuilder.BuildRevertGlobalOptionToDefault(option),
            ct);

    public Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.ShowGlobalStatus, ct);

    public async Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsTcpStateCommandBuilder.FileName, WindowsTcpStateCommandBuilder.Arguments, ct);
        return result.Success ? WindowsTcpStateParser.Parse(result.StandardOutput) : TcpSettingsSnapshot.Unknown;
    }

    public Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildSetAutoTuningLevel(level), ct);

    public async Task<IReadOnlyList<CommandExecutionResult>> SetMtuAsync(string adapterId, int mtu, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsTcpCommandBuilder.BuildSetMtu(adapterId, mtu))
        {
            results.Add(await executor.RunAsync(WindowsTcpCommandBuilder.FileName, args, ct));
        }

        return results;
    }

    public async Task<int?> GetMtuAsync(string adapterId, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(
            WindowsMtuStateCommandBuilder.FileName,
            WindowsMtuStateCommandBuilder.BuildArguments(adapterId),
            ct);

        return result.Success ? WindowsMtuStateParser.Parse(result.StandardOutput) : null;
    }
}
