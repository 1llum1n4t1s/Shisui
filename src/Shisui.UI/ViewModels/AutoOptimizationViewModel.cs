using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.UI.ViewModels;

/// <summary>安全なクイック最適化と、使い込んだPC向け診断を一か所に集約する。</summary>
public partial class AutoOptimizationViewModel : ObservableObject
{
    private readonly INetworkMutationGate _networkMutationGate;
    private readonly ILegacyNetworkDiagnosticsService? _legacyNetworkDiagnosticsService;
    private readonly TcpTuningViewModel? _tcpTuningViewModel;
    private readonly IGamingNetworkProfileService? _gamingNetworkProfileService;

    public AutoOptimizationViewModel(
        DnsSettingsViewModel dnsSettings,
        INetworkMutationGate networkMutationGate,
        ILegacyNetworkDiagnosticsService? legacyNetworkDiagnosticsService = null,
        TcpTuningViewModel? tcpTuningViewModel = null,
        IGamingNetworkProfileService? gamingNetworkProfileService = null)
    {
        DnsSettings = dnsSettings;
        _networkMutationGate = networkMutationGate;
        _legacyNetworkDiagnosticsService = legacyNetworkDiagnosticsService;
        _tcpTuningViewModel = tcpTuningViewModel;
        _gamingNetworkProfileService = gamingNetworkProfileService;

        DnsSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DnsSettingsViewModel.IsBusy))
            {
                OnPropertyChanged(nameof(IsOperationRunning));
            }
            else if (e.PropertyName == nameof(DnsSettingsViewModel.SelectedAdapter))
            {
                LegacyNetworkFindings.Clear();
                LegacyDiagnosticsStatusText = string.Empty;
                HasLegacyDiagnosticsWarnings = false;
                CanResetSelectedNic = false;
                GamingProfileStatusText = string.Empty;
            }
        };
    }

    public event EventHandler<CommandExecutionResult>? CommandExecuted;
    public DnsSettingsViewModel DnsSettings { get; }
    public ObservableCollection<LegacyNetworkDiagnosticFinding> LegacyNetworkFindings { get; } = [];

    public bool IsLegacyDiagnosticsAvailable => _legacyNetworkDiagnosticsService is not null;
    public bool IsGamingProfileAvailable => _gamingNetworkProfileService is not null;

    public bool IsOperationRunning =>
        DnsSettings.IsBusy || IsLegacyDiagnosticsRunning || IsResettingNic || IsGamingProfileRunning;

    [ObservableProperty] private bool isLegacyDiagnosticsRunning;
    [ObservableProperty] private bool isResettingNic;
    [ObservableProperty] private bool hasLegacyDiagnosticsWarnings;
    [ObservableProperty] private bool canResetSelectedNic;
    [ObservableProperty] private string legacyDiagnosticsStatusText = string.Empty;
    [ObservableProperty] private bool isGamingProfileRunning;
    [ObservableProperty] private string gamingProfileStatusText = string.Empty;

    partial void OnIsLegacyDiagnosticsRunningChanged(bool value) => NotifyOperationState();
    partial void OnIsResettingNicChanged(bool value) => NotifyOperationState();
    partial void OnIsGamingProfileRunningChanged(bool value) => NotifyOperationState();

    [RelayCommand]
    private Task ApplyGamingProfileAsync() => RunGamingProfileAsync(restore: false);

    [RelayCommand]
    private Task RestoreGamingProfileAsync() => RunGamingProfileAsync(restore: true);

    private async Task RunGamingProfileAsync(bool restore)
    {
        if (IsOperationRunning || _gamingNetworkProfileService is null)
        {
            return;
        }

        if (DnsSettings.SelectedAdapter is not { } adapter)
        {
            GamingProfileStatusText = "対象の有線またはWi-Fiネットワークアダプターを選択してください";
            return;
        }

        IsGamingProfileRunning = true;
        using var operation = LoggerBootstrap.BeginOperation(
            restore ? "ゲーム向けNIC設定の復元" : "ゲーム向け低遅延NIC設定", adapter.Id);
        try
        {
            // service が永続復元情報の保存から読み戻しまで同じゲートを保持する。
            var results = restore
                ? await _gamingNetworkProfileService.RestoreAsync(adapter.Id)
                : await _gamingNetworkProfileService.ApplyAsync(adapter.Id);
            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            GamingProfileStatusText = results.Count == 0
                ? "変更結果を取得できませんでした。実行ログを確認してください"
                : string.Join(Environment.NewLine, results.Where(r => r.DiagnosticId is null)
                    .Select(r => r.Success ? r.StandardOutput : r.StandardError));
            if (results.Any(r => !r.Success))
            {
                GamingProfileStatusText = "未適用または失敗した項目があります。実行ログを確認してください。\n" + GamingProfileStatusText;
            }
            GamingProfileStatusText = $"対象: {adapter.DisplayName}\n{GamingProfileStatusText}";
        }
        catch (Exception ex)
        {
            GamingProfileStatusText = $"対象: {adapter.DisplayName}\nゲーム向けNIC設定を完了できませんでした: {ex.Message}";
            LoggerBootstrap.Log.Error("ゲーム向けNIC設定に失敗しました", ex);
        }
        finally
        {
            IsGamingProfileRunning = false;
        }
    }

    private void NotifyOperationState() =>
        OnPropertyChanged(nameof(IsOperationRunning));

    [RelayCommand]
    private async Task RunQuickOptimizationAsync()
    {
        await DnsSettings.RunOneClickOptimizationAsync();
        if (_tcpTuningViewModel is not null)
        {
            await _tcpTuningViewModel.RefreshStateAfterExternalChangeAsync();
        }
    }

    [RelayCommand]
    private async Task RunLegacyNetworkDiagnosticsAsync()
    {
        if (_legacyNetworkDiagnosticsService is null)
        {
            LegacyDiagnosticsStatusText = "使い込んだPC向け診断はWindowsでのみ利用できます";
            return;
        }

        if (DnsSettings.SelectedAdapter is not { } adapter)
        {
            LegacyDiagnosticsStatusText = "診断するネットワークアダプターを選択してください";
            return;
        }

        IsLegacyDiagnosticsRunning = true;
        LegacyNetworkFindings.Clear();
        HasLegacyDiagnosticsWarnings = false;
        CanResetSelectedNic = false;
        LegacyDiagnosticsStatusText = "NIC・ドライバー・古いネットワーク設定を診断しています…";
        try
        {
            var report = await _legacyNetworkDiagnosticsService.DiagnoseAsync(adapter.Id);
            foreach (var finding in report.Findings)
            {
                LegacyNetworkFindings.Add(finding);
            }

            HasLegacyDiagnosticsWarnings = report.HasWarnings;
            CanResetSelectedNic = report.RecommendNicReset;
            LegacyDiagnosticsStatusText = report.HasWarnings
                ? $"{report.Findings.Count(f => f.IsWarning)} 件の確認項目があります。案内に沿って必要なものだけ修復してください"
                : "診断が完了しました。明確なネットワーク残骸は見つかりませんでした";
        }
        catch (Exception ex)
        {
            LegacyDiagnosticsStatusText = $"診断に失敗しました: {ex.Message}";
            LoggerBootstrap.Log.Error("使い込んだPC向けネットワーク診断に失敗しました", ex);
        }
        finally
        {
            IsLegacyDiagnosticsRunning = false;
        }
    }

    [RelayCommand]
    private async Task ResetSelectedNicAdvancedPropertiesAsync()
    {
        if (_legacyNetworkDiagnosticsService is null || DnsSettings.SelectedAdapter is not { } adapter ||
            !CanResetSelectedNic)
        {
            return;
        }

        IsResettingNic = true;
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var result = await _legacyNetworkDiagnosticsService.ResetAdapterAdvancedPropertiesAsync(adapter.Id);
            CommandExecuted?.Invoke(this, result);
            LegacyDiagnosticsStatusText = result.Success
                ? $"{adapter.DisplayName} のNIC詳細設定を工場出荷値へ戻しました。接続が戻ってから再診断してください"
                : $"{adapter.DisplayName} のNIC詳細設定を初期化できませんでした。実行ログを確認してください";
            if (result.Success)
            {
                CanResetSelectedNic = false;
            }
        }
        catch (Exception ex)
        {
            LegacyDiagnosticsStatusText = $"NIC詳細設定の初期化に失敗しました: {ex.Message}";
            LoggerBootstrap.Log.Error("NIC詳細設定の初期化に失敗しました", ex);
        }
        finally
        {
            IsResettingNic = false;
        }
    }
}
