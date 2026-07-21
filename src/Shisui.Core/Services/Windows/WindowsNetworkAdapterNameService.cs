using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkAdapterNameService(
    ICommandExecutor executor,
    IGhostAdapterService ghostAdapterService) : INetworkAdapterNameService
{
    public async Task<NetworkAdapterNameCleanupResult> CleanupAsync(
        string? currentName,
        CancellationToken ct = default)
    {
        var targetName = string.IsNullOrWhiteSpace(currentName)
            ? null
            : WindowsAdapterNameNormalizer.GetBaseConnectionName(currentName);
        var ghosts = await ghostAdapterService.GetGhostAdaptersAsync(ct);
        List<CommandExecutionResult> commandResults = [];
        var removedCount = 0;
        var failedCount = 0;
        foreach (var ghost in ghosts.DistinctBy(
                     ghost => ghost.InstanceId,
                     StringComparer.OrdinalIgnoreCase))
        {
            var removeResult = await ghostAdapterService.RemoveGhostAdapterAsync(ghost.InstanceId, ct);
            commandResults.Add(removeResult);
            if (removeResult.Success)
            {
                removedCount++;
            }
            else
            {
                failedCount++;
            }
        }

        // セーフモード等で現在のアダプタ一覧が空でも、切断済み登録の全削除までは完了させる。
        // 接続名の変更は、通常起動時に対象アダプタが選択されている場合だけ追加で行う。
        if (currentName is null || targetName is null)
        {
            return CreateResult(
                currentName,
                targetName,
                removedCount,
                failedCount,
                false,
                commandResults);
        }

        if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return CreateResult(
                currentName,
                targetName,
                removedCount,
                failedCount,
                false,
                commandResults);
        }

        var queryResult = await executor.RunAsync(
            WindowsAdapterNameCommandBuilder.FileName,
            WindowsAdapterNameCommandBuilder.QueryArguments,
            ct);
        if (!queryResult.Success)
        {
            return CreateResult(
                currentName,
                targetName,
                removedCount,
                failedCount,
                false,
                commandResults,
                $"接続名一覧を取得できませんでした: {queryResult.StandardError}");
        }

        var adapterRecords = WindowsAdapterNameParser.Parse(queryResult.StandardOutput);
        if (!adapterRecords.Any(record =>
                string.Equals(record.Name, currentName, StringComparison.OrdinalIgnoreCase)))
        {
            return CreateResult(
                currentName,
                targetName,
                removedCount,
                failedCount,
                false,
                commandResults,
                "選択中のネットワークアダプタを Windows の接続名一覧で確認できませんでした");
        }

        if (adapterRecords.Any(record =>
                string.Equals(record.Name, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            return CreateResult(
                currentName,
                targetName,
                removedCount,
                failedCount,
                false,
                commandResults,
                $"接続名「{targetName}」は別の現役アダプタ、または削除できなかった旧登録で使用されています");
        }

        var renameResult = await executor.RunAsync(
            WindowsAdapterNameCommandBuilder.FileName,
            WindowsAdapterNameCommandBuilder.BuildRenameArguments(currentName, targetName),
            ct);
        commandResults.Add(renameResult);
        return CreateResult(
            currentName,
            targetName,
            removedCount,
            failedCount,
            renameResult.Success,
            commandResults,
            renameResult.Success ? null : "接続名を変更できませんでした");
    }

    private static NetworkAdapterNameCleanupResult CreateResult(
        string? originalName,
        string? targetName,
        int removedGhostCount,
        int failedGhostCount,
        bool wasRenamed,
        IReadOnlyList<CommandExecutionResult> commandResults,
        string? operationError = null)
    {
        List<string> errors = [];
        if (failedGhostCount > 0)
        {
            errors.Add($"切断済みの旧デバイス {failedGhostCount} 件を削除できませんでした");
        }

        if (!string.IsNullOrWhiteSpace(operationError))
        {
            errors.Add(operationError);
        }

        var errorMessage = errors.Count == 0 ? null : string.Join("。", errors);
        return new NetworkAdapterNameCleanupResult(
            errorMessage is null,
            originalName,
            targetName,
            removedGhostCount,
            failedGhostCount,
            wasRenamed,
            commandResults,
            errorMessage);
    }
}
