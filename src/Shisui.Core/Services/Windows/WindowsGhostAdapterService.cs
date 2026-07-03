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
            return [];
        }

        return WindowsGhostAdapterParser.Parse(result.StandardOutput)
            .OrderBy(a => a.IsLikelyMicrosoftVirtualDevice)
            .ThenBy(a => a.Description, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<CommandExecutionResult> RemoveGhostAdapterAsync(string instanceId, CancellationToken ct = default) =>
        executor.RunAsync(WindowsGhostAdapterCommandBuilder.FileName, WindowsGhostAdapterCommandBuilder.BuildRemove(instanceId), ct);
}
