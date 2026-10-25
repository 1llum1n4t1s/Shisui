using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsTcpTuningService(ICommandExecutor executor) : ITcpTuningService
{
    public async Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildEnableBbr2(), ct);
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> RevertBbr2ToDefaultAsync(CancellationToken ct = default)
    {
        return await WindowsCommandSequence.RunAsync(executor, WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildRevertBbr2ToDefault(), ct);
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
        return await WindowsCommandSequence.RunAsync(executor, WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildRevertGlobalOptionsToDefault(), ct);
    }

    public Task<CommandExecutionResult> RevertLegacyTcpRegistryTweaksToDefaultAsync(CancellationToken ct = default) =>
        executor.RunAsync(
            WindowsLegacyTcpRegistryCommandBuilder.FileName,
            WindowsLegacyTcpRegistryCommandBuilder.Arguments,
            ct);

    public Task<IReadOnlyList<CommandExecutionResult>> ApplyLowLatencyGlobalOptionsAsync(CancellationToken ct = default) =>
        WindowsCommandSequence.RunAsync(executor, WindowsTcpCommandBuilder.FileName,
            WindowsTcpCommandBuilder.BuildLowLatencyGlobalOptions(), ct);

    public Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildSetGlobalOption(option, enabled), ct);

    public async Task<CommandExecutionResult> EnableTcpPacingAsync(CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsTcpPacingCommandBuilder.FileName,
            WindowsTcpPacingCommandBuilder.BuildEnableArguments(), ct);
        if (!result.Success || WindowsTcpPacingCommandBuilder.IsVerifiedOutput(result.StandardOutput))
            return result;

        return result with
        {
            Success = false,
            StandardError = result.StandardError + Environment.NewLine + "TCP ペーシングの適用結果を確認できませんでした。",
        };
    }

    public Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default) =>
        executor.RunAsync(WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.ShowGlobalStatus, ct);

    public async Task<CommandExecutionResult> SetTcpAckFrequencyAsync(string adapterId, bool enabled, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsTcpAckFrequencyCommandBuilder.FileName,
            WindowsTcpAckFrequencyCommandBuilder.BuildSetArguments(adapterId, enabled), ct);
        if (!result.Success)
            return result;

        var state = WindowsTcpAckFrequencyCommandBuilder.ParseState(result.StandardOutput);
        if (state.IsKnown && state.Value == (enabled ? 1 : (int?)null))
            return result;

        return result with
        {
            Success = false,
            StandardError = result.StandardError + Environment.NewLine + "TcpAckFrequency の変更結果を確認できませんでした。",
        };
    }

    public async Task<TcpAckFrequencyState> GetTcpAckFrequencyAsync(string adapterId, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsTcpAckFrequencyCommandBuilder.FileName,
            WindowsTcpAckFrequencyCommandBuilder.BuildGetArguments(adapterId), ct);
        return result.Success ? WindowsTcpAckFrequencyCommandBuilder.ParseState(result.StandardOutput) : TcpAckFrequencyState.Unknown;
    }

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
        return await WindowsCommandSequence.RunAsync(executor, WindowsTcpCommandBuilder.FileName, WindowsTcpCommandBuilder.BuildRevertMtuToDefault(adapterId), ct);
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
