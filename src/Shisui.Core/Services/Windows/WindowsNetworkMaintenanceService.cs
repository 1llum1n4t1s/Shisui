using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkMaintenanceService(ICommandExecutor executor) : INetworkMaintenanceService
{
    public IReadOnlyList<MaintenanceCommandDefinition> GetAvailableCommands() =>
        WindowsMaintenanceCommandCatalog.All.Select(c => c.Definition).ToList();

    public IReadOnlyDictionary<string, string> GetBatchableCategoryLabels() =>
        WindowsMaintenanceCommandCatalog.BatchableCategoryLabels;

    public Task<CommandExecutionResult> RunAsync(string commandId, CancellationToken ct = default)
    {
        var command = WindowsMaintenanceCommandCatalog.Find(commandId);
        if (command is null)
        {
            return Task.FromResult(CommandExecutionResult.Skipped($"未知のコマンド ID: {commandId}"));
        }

        return executor.RunAsync(command.FileName, command.Arguments, ct);
    }
}
