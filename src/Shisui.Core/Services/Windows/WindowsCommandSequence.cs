using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

internal static class WindowsCommandSequence
{
    internal static async Task<IReadOnlyList<CommandExecutionResult>> RunAsync(
        ICommandExecutor executor,
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken ct)
    {
        var results = new List<CommandExecutionResult>();
        foreach (var args in arguments)
        {
            results.Add(await executor.RunAsync(fileName, args, ct));
        }

        return results;
    }
}
