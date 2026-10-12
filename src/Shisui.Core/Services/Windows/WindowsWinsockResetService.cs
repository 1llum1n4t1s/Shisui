using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsWinsockResetService(
    ICommandExecutor executor,
    IWinsockCatalogInspector inspector) : IWinsockResetService
{
    public async Task<CommandExecutionResult> ResetIfNoLayeredProvidersAsync(CancellationToken ct = default)
    {
        var trace = new CommandExecutionTrace("Winsock リセット: LSP 登録を事前確認");
        try
        {
            ct.ThrowIfCancellationRequested();
            var inspection = await inspector.InspectAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (!inspection.IsComplete)
            {
                return trace.Complete(InspectionFailure(inspection.Detail));
            }

            if (inspection.HasLayeredProviders)
            {
                return trace.Complete(new(true, "Winsock リセット: LSP 登録を保護して省略", 0,
                    $"リセットは実行していません。{inspection.Detail}", string.Empty));
            }

            trace.Complete(new(true, "Winsock リセット: LSP 登録を事前確認", 0,
                $"LSP 登録がないため、リセットを許可します。{inspection.Detail}", string.Empty));
        }
        catch (OperationCanceledException ex)
        {
            trace.Interrupted(ex);
            throw;
        }
        catch (Exception ex)
        {
            trace.Interrupted(ex);
            return trace.Complete(InspectionFailure($"LSP 登録を確認できませんでした ({ex.GetType().Name})。"));
        }

        try
        {
            return await executor.RunAsync("netsh", "winsock reset", ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, "netsh winsock reset", -1, string.Empty,
                $"Winsock リセットの実行結果を確認できませんでした ({ex.GetType().Name})。");
        }
    }

    private static CommandExecutionResult InspectionFailure(string detail) =>
        new(false, "Winsock リセット: LSP 登録を確認できず未実施", -1, string.Empty,
            $"リセットは実行していません。{detail}");
}
