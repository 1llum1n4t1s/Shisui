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

    public Task<CommandExecutionResult> ResetAllTcpSettingsToDefaultAsync(CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.ResetAllToDefault, ct);

    public async Task<IReadOnlyList<CommandExecutionResult>> EnableLossRecoveryAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var template in WindowsTcpCommandBuilder.SupplementalTemplates.Where(value => value != "Compat"))
        {
            var state = await executor.RunAsync(WindowsTcpCommandBuilder.FileName,
                $"int tcp show supplemental template={template}", ct);
            if (state.Success && WindowsLossRecoveryStateParser.AreBothEnabled(state.StandardOutput))
            {
                // Windows によっては読み取り可能でも書き込みを拒否する。既有効なら再設定しない。
                results.Add(new CommandExecutionResult(true, $"RACK / TLP 確認 ({template}): 既に有効・変更不要",
                    0, state.StandardOutput, string.Empty));
                continue;
            }

            results.Add(await executor.RunAsync(WindowsTcpCommandBuilder.FileName,
                WindowsTcpCommandBuilder.BuildEnableLossRecovery(template), ct));
        }

        return results;
    }

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

    public Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.ShowGlobalStatus, ct);

    public async Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsTcpStateCommandBuilder.FileName, WindowsTcpStateCommandBuilder.Arguments, ct);
        return result.Success ? WindowsTcpStateParser.Parse(result.StandardOutput) : TcpSettingsSnapshot.Unknown;
    }

    public Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildSetAutoTuningLevel(level), ct);

    public async Task<IReadOnlyList<CommandExecutionResult>> RevertMtuToDefaultAsync(
        string adapterId,
        CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in WindowsTcpCommandBuilder.BuildRevertMtuToDefault(adapterId))
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
