using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsLegacyNetworkDiagnosticsService(
    ICommandExecutor executor,
    IGhostAdapterService ghostAdapterService) : ILegacyNetworkDiagnosticsService
{
    private const double PacketProblemRateThreshold = 0.001;
    private const ulong MinimumDiscardCountForRateWarning = 100;

    public async Task<LegacyNetworkDiagnosticsReport> DiagnoseAsync(
        string adapterName,
        CancellationToken ct = default)
    {
        var adapterTask = executor.RunAsync(
            WindowsLegacyNetworkDiagnosticsCommandBuilder.PowerShellFileName,
            WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildAdapterSnapshotArguments(adapterName),
            ct);
        var winsockTask = executor.RunAsync(
            WindowsLegacyNetworkDiagnosticsCommandBuilder.WinsockFileName,
            WindowsLegacyNetworkDiagnosticsCommandBuilder.WinsockArguments,
            ct);
        var problemDevicesTask = executor.RunAsync(
            WindowsLegacyNetworkDiagnosticsCommandBuilder.ProblemDevicesFileName,
            WindowsLegacyNetworkDiagnosticsCommandBuilder.ProblemDevicesArguments,
            ct);

        await Task.WhenAll(adapterTask, winsockTask, problemDevicesTask);

        IReadOnlyList<GhostAdapterInfo> ghostAdapters = [];
        string? ghostError = null;
        try
        {
            ghostAdapters = await ghostAdapterService.GetGhostAdaptersAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ghostError = ex.Message;
        }

        var adapterResult = await adapterTask;
        var winsockResult = await winsockTask;
        var problemDevicesResult = await problemDevicesTask;
        var parsedAdapter = adapterResult.Success
            ? WindowsLegacyNetworkDiagnosticsParser.ParseAdapterSnapshot(adapterResult.StandardOutput)
            : null;
        var adapter = parsedAdapter?.Description is not null ? parsedAdapter : null;
        var winsockEnabled = winsockResult.Success
            ? WindowsLegacyNetworkDiagnosticsParser.ParseWinsockSendAutoTuning(winsockResult.StandardOutput)
            : null;
        var problemDeviceCount = 0;
        var parsedProblemDevices = problemDevicesResult.Success &&
            WindowsLegacyNetworkDiagnosticsParser.TryParseProblemDeviceCount(
                problemDevicesResult.StandardOutput,
                out problemDeviceCount);

        var findings = BuildFindings(
            adapter,
            adapterResult.Success
                ? adapter is null ? "NIC統計の出力を解釈できませんでした" : null
                : adapterResult.StandardError,
            winsockEnabled,
            winsockResult.Success
                ? winsockEnabled is null ? "Winsock状態の出力を解釈できませんでした" : null
                : winsockResult.StandardError,
            problemDeviceCount,
            problemDevicesResult.Success
                ? parsedProblemDevices ? null : "問題デバイス一覧のXMLを解釈できませんでした"
                : problemDevicesResult.StandardError,
            ghostAdapters.Count,
            ghostError);

        return new LegacyNetworkDiagnosticsReport(
            adapterName,
            adapter,
            winsockEnabled,
            problemDeviceCount,
            ghostAdapters.Count,
            findings);
    }

    public Task<CommandExecutionResult> ResetAdapterAdvancedPropertiesAsync(
        string adapterName,
        CancellationToken ct = default) =>
        executor.RunAsync(
            WindowsLegacyNetworkDiagnosticsCommandBuilder.PowerShellFileName,
            WindowsLegacyNetworkDiagnosticsCommandBuilder.BuildResetAdapterAdvancedPropertiesArguments(adapterName),
            ct);

    private static IReadOnlyList<LegacyNetworkDiagnosticFinding> BuildFindings(
        LegacyNetworkAdapterSnapshot? adapter,
        string? adapterError,
        bool? winsockEnabled,
        string? winsockError,
        int problemDeviceCount,
        string? problemDevicesError,
        int ghostAdapterCount,
        string? ghostError)
    {
        var findings = new List<LegacyNetworkDiagnosticFinding>();

        if (adapter is null)
        {
            findings.Add(new(
                "NIC統計を取得できませんでした",
                string.IsNullOrWhiteSpace(adapterError) ? "選択中のアダプターを確認してください" : adapterError.Trim(),
                true));
        }
        else
        {
            var driver = adapter.DriverDate is { } date
                ? $"ドライバー {adapter.DriverVersion ?? "不明"} ({date:yyyy-MM-dd})"
                : $"ドライバー {adapter.DriverVersion ?? "不明"}";
            findings.Add(new(
                "選択中NIC",
                $"{adapter.Description ?? "詳細不明"} / {adapter.LinkSpeed ?? "リンク速度不明"} / {driver}",
                false));

            if (adapter.DriverDate is { } driverDate && driverDate < DateTime.Today.AddYears(-5))
            {
                findings.Add(new(
                    "NICドライバーが古い可能性があります",
                    $"ドライバー日付は {driverDate:yyyy-MM-dd} です。PCまたはNICメーカーの最新版を確認してください",
                    true,
                    LegacyNetworkRepairPath.DriverUpdate));
            }

            var errors = adapter.ReceivedPacketErrors + adapter.OutboundPacketErrors;
            var discards = adapter.ReceivedDiscardedPackets + adapter.OutboundDiscardedPackets;
            var packets = adapter.ReceivedPackets + adapter.SentPackets;
            var problemRate = packets == 0 ? 0 : (double)(errors + discards) / packets;
            if (errors > 0 || (discards >= MinimumDiscardCountForRateWarning && problemRate >= PacketProblemRateThreshold))
            {
                findings.Add(new(
                    "NICでエラーまたは破棄パケットが発生しています",
                    $"エラー {errors:N0} / 破棄 {discards:N0} / 総パケット {packets:N0}。ケーブル・電波・ドライバーを先に確認してください",
                    true,
                    LegacyNetworkRepairPath.NicAdvancedPropertiesReset));
            }
            else
            {
                findings.Add(new(
                    "NICパケット統計",
                    $"重大なエラー傾向はありません (エラー {errors:N0} / 破棄 {discards:N0})",
                    false));
            }

            if (adapter.TaskOffloadDisabled is true)
            {
                findings.Add(new(
                    "TCP/IPタスクオフロードが明示的に無効です",
                    "古い高速化ツールの DisableTaskOffload=1 が残っている可能性があります。NIC固有の回避設定か確認してから変更してください",
                    true));
            }
        }

        if (winsockEnabled is false)
        {
            findings.Add(new(
                "Winsock送信自動調整が無効です",
                "送信バッファーの自動調整が停止しています",
                true,
                LegacyNetworkRepairPath.QuickOptimization));
        }
        else if (winsockEnabled is true)
        {
            findings.Add(new("Winsock送信自動調整", "有効です", false));
        }
        else if (!string.IsNullOrWhiteSpace(winsockError))
        {
            findings.Add(new("Winsock状態を取得できませんでした", winsockError.Trim(), true));
        }

        if (problemDeviceCount > 0)
        {
            findings.Add(new(
                "問題状態のネットワークデバイスがあります",
                $"Windowsが問題を報告しているネットワークデバイスが {problemDeviceCount} 件あります。デバイスマネージャーとドライバーを確認してください",
                true,
                LegacyNetworkRepairPath.NicAdvancedPropertiesReset));
        }
        else if (!string.IsNullOrWhiteSpace(problemDevicesError))
        {
            findings.Add(new("デバイス問題を取得できませんでした", problemDevicesError.Trim(), true));
        }

        if (ghostAdapterCount > 0)
        {
            findings.Add(new(
                "切断済みネットワークデバイスがあります",
                $"取り外したNICや旧VPNの候補が {ghostAdapterCount} 件あります",
                true,
                LegacyNetworkRepairPath.GhostAdapters));
        }
        else if (!string.IsNullOrWhiteSpace(ghostError))
        {
            findings.Add(new("切断済みデバイスを取得できませんでした", ghostError, true));
        }

        if (findings.All(f => !f.IsWarning))
        {
            findings.Add(new("診断結果", "使い込みによる明確なネットワーク残骸は見つかりませんでした", false));
        }

        return findings;
    }
}
