using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkMaintenanceService(ICommandExecutor executor) : INetworkMaintenanceService
{
    private const string PcieLinkStateCommandId = "powercfg-pcie-link-state-ac-off";

    public IReadOnlyList<MaintenanceCommandDefinition> GetAvailableCommands() =>
        WindowsMaintenanceCommandCatalog.All.Select(c => c.Definition).ToList();

    public IReadOnlyDictionary<string, string> GetBatchableCategoryLabels() =>
        WindowsMaintenanceCommandCatalog.BatchableCategoryLabels;

    public async Task<CommandExecutionResult> RunAsync(string commandId, CancellationToken ct = default)
    {
        var command = WindowsMaintenanceCommandCatalog.Find(commandId);
        if (command is null)
        {
            return CommandExecutionResult.Skipped($"未知のコマンド ID: {commandId}");
        }

        return commandId == PcieLinkStateCommandId
            ? await DisablePcieLinkStatePowerManagementOnAcAsync(ct)
            : await executor.RunAsync(command.FileName, command.Arguments, ct);
    }

    private async Task<CommandExecutionResult> DisablePcieLinkStatePowerManagementOnAcAsync(CancellationToken ct)
    {
        var activeResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, ct);
        if (!activeResult.Success)
        {
            return activeResult;
        }

        if (!WindowsPowerPlanCommandBuilder.TryParseActiveScheme(activeResult.StandardOutput, out var schemeGuid))
        {
            return VerificationFailure("現在の電源プランを一意に確認できなかったため、設定を変更しませんでした。", activeResult.StandardOutput);
        }

        var beforeResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.QueryLinkStateArguments(schemeGuid), ct);
        if (!beforeResult.Success)
        {
            return beforeResult;
        }

        if (!WindowsPowerPlanCommandBuilder.TryParseLinkStateValues(beforeResult.StandardOutput, schemeGuid, out var acBefore, out var dcBefore))
        {
            return VerificationFailure("PCI Express の AC/DC 設定値を確認できなかったため、設定を変更しませんでした。", beforeResult.StandardOutput);
        }

        var unchangedResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, ct);
        if (!unchangedResult.Success)
        {
            return unchangedResult;
        }

        if (!WindowsPowerPlanCommandBuilder.TryParseActiveScheme(unchangedResult.StandardOutput, out var unchangedGuid)
            || unchangedGuid != schemeGuid)
        {
            return VerificationFailure("確認中に現在の電源プランが変わったため、設定を変更しませんでした。", unchangedResult.StandardOutput);
        }

        var setResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.SetAcLinkStateArguments(schemeGuid, 0), ct);
        if (!setResult.Success)
        {
            return setResult;
        }

        try
        {
            // 書き込み直後にも確認し、外部で選択された別プランを setactive で上書きしない。
            var beforeActivationResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, ct);
            if (!beforeActivationResult.Success
                || !WindowsPowerPlanCommandBuilder.TryParseActiveScheme(beforeActivationResult.StandardOutput, out var beforeActivationGuid)
                || beforeActivationGuid != schemeGuid)
            {
                await RollBackAcValueAsync(schemeGuid, acBefore);
                return beforeActivationResult.Success
                    ? VerificationFailure("設定中に現在の電源プランが変わったため、変更前の AC 値への復元を試みました。復元結果は個別コマンドログを確認してください。", beforeActivationResult.StandardOutput)
                    : beforeActivationResult;
            }

            var activateResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.ActivateSchemeArguments(schemeGuid), ct);
            if (!activateResult.Success)
            {
                await RollBackAcValueAsync(schemeGuid, acBefore);
                return activateResult;
            }

            var finalActiveResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, ct);
            if (!finalActiveResult.Success
                || !WindowsPowerPlanCommandBuilder.TryParseActiveScheme(finalActiveResult.StandardOutput, out var finalSchemeGuid)
                || finalSchemeGuid != schemeGuid)
            {
                await RollBackAcValueAsync(schemeGuid, acBefore);
                return finalActiveResult.Success
                    ? VerificationFailure("適用後に現在の電源プランを確認できませんでした。", finalActiveResult.StandardOutput)
                    : finalActiveResult;
            }

            var afterResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.QueryLinkStateArguments(schemeGuid), ct);
            if (!afterResult.Success)
            {
                await RollBackAcValueAsync(schemeGuid, acBefore);
                return afterResult;
            }

            if (!WindowsPowerPlanCommandBuilder.TryParseLinkStateValues(afterResult.StandardOutput, schemeGuid, out var acAfter, out var dcAfter)
                || acAfter != 0 || dcAfter != dcBefore)
            {
                await RollBackAcValueAsync(schemeGuid, acBefore);
                return VerificationFailure(
                    "適用後の AC=オフ、DC=変更前の値を確認できなかったため、変更前の AC 値への復元を試みました。復元結果は個別コマンドログを確認してください。",
                    afterResult.StandardOutput);
            }

            return new CommandExecutionResult(
                true,
                "powercfg PCI Express リンク状態電源管理 (AC のみオフ)",
                0,
                $"SCHEME={schemeGuid:D}{Environment.NewLine}AC_BEFORE={acBefore}{Environment.NewLine}AC_AFTER={acAfter}{Environment.NewLine}DC={dcAfter}",
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            await RollBackAcValueAsync(schemeGuid, acBefore);
            throw;
        }
    }

    private Task<CommandExecutionResult> RunPowerCfgAsync(string arguments, CancellationToken ct) =>
        executor.RunAsync("powercfg", arguments, ct);

    private async Task<bool> RollBackAcValueAsync(Guid schemeGuid, uint acBefore)
    {
        var restoreResult = await RunPowerCfgAsync(
            WindowsPowerPlanCommandBuilder.SetAcLinkStateArguments(schemeGuid, acBefore), CancellationToken.None);
        if (!restoreResult.Success)
        {
            return false;
        }

        var activeResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, CancellationToken.None);
        if (!activeResult.Success
            || !WindowsPowerPlanCommandBuilder.TryParseActiveScheme(activeResult.StandardOutput, out var activeGuid))
        {
            return false;
        }

        return activeGuid != schemeGuid
            || (await RunPowerCfgAsync(
                WindowsPowerPlanCommandBuilder.ActivateSchemeArguments(schemeGuid), CancellationToken.None)).Success;
    }

    private static CommandExecutionResult VerificationFailure(string message, string output) =>
        new(false, "powercfg PCI Express リンク状態電源管理 (AC のみオフ)", -1, output, message);
}
