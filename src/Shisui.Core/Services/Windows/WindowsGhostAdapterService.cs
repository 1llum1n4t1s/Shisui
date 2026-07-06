using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsGhostAdapterService(ICommandExecutor executor) : IGhostAdapterService
{
    public async Task<IReadOnlyList<GhostAdapterInfo>> GetGhostAdaptersAsync(CancellationToken ct = default)
    {
        var result = await executor.RunAsync(WindowsGhostAdapterCommandBuilder.FileName, WindowsGhostAdapterCommandBuilder.ListArguments, ct);
        if (!result.Success)
        {
            // 「0 件」と「コマンド自体が失敗」を呼び出し元が区別できるよう例外にする (2026-07-06 /rere
            // レビューで発見: 従来は空リストを返し、両者が同じ「見つかりませんでした」表示になっていた)。
            throw new InvalidOperationException($"切断済みデバイス一覧の取得コマンドが失敗しました: {result.StandardError}");
        }

        return WindowsGhostAdapterParser.Parse(result.StandardOutput)
            .OrderBy(a => a.IsLikelyMicrosoftVirtualDevice)
            .ThenBy(a => a.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<CommandExecutionResult> RemoveGhostAdapterAsync(string instanceId, CancellationToken ct = default) =>
        executor.RunAsync(WindowsGhostAdapterCommandBuilder.FileName, WindowsGhostAdapterCommandBuilder.BuildRemove(instanceId), ct);
}
