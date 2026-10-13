using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public bool IsWindows { get; } = OperatingSystem.IsWindows();

    public bool IsMacOS { get; } = OperatingSystem.IsMacOS();

    public DnsSettingsViewModel DnsSettings { get; }

    /// <summary>クイック最適化と、使い込んだPC向け診断を集約したタブ。</summary>
    public AutoOptimizationViewModel AutoOptimization { get; }

    /// <summary>Ping / トレースルート診断タブ (Windows/macOS 両対応、常に非 null)。</summary>
    public NetworkDiagnosticsViewModel NetworkDiagnostics { get; }

    /// <summary>Windows でのみ非 null。View 側は null を「タブ非表示」の合図として扱う。</summary>
    public TcpTuningViewModel? TcpTuning { get; }

    /// <summary>Windows でのみ非 null。View 側は null を「タブ非表示」の合図として扱う。</summary>
    public MaintenanceViewModel? Maintenance { get; }

    public VersionViewModel Version { get; }

    public ObservableCollection<CommandLogEntry> LogEntries { get; } = [];

    /// <summary>各画面の処理中は、ログを含むウィンドウ全体の操作を止める。</summary>
    public bool IsProcessing =>
        DnsSettings.IsBusy || DnsSettings.IsPinging || DnsSettings.IsAdapterNameBusy ||
        AutoOptimization.IsOperationRunning || AutoOptimization.RunQuickOptimizationCommand.IsRunning ||
        NetworkDiagnostics.IsBusy ||
        TcpTuning is { IsBusy: true } || TcpTuning is { IsMtuBusy: true } ||
        Maintenance?.Categories.Any(group => group.IsRunningAll || group.Items.Any(item => item.IsRunning)) == true ||
        Version.IsChecking || Version.IsSavingSettings || Version.ClearIgnoredUpdateTagCommand.IsRunning;

    public string ProcessingText => string.IsNullOrWhiteSpace(DnsSettings.OneClickProgressText)
        ? "処理が終わるまでお待ちください"
        : DnsSettings.OneClickProgressText;

    public MainWindowViewModel(
        DnsSettingsViewModel dnsSettings,
        NetworkDiagnosticsViewModel networkDiagnostics,
        AutoOptimizationViewModel autoOptimization,
        VersionViewModel version,
        TcpTuningViewModel? tcpTuning = null,
        MaintenanceViewModel? maintenance = null)
    {
        Version = version;

        // TcpTuning / Maintenance は Windows でしか DI 登録されない (macOS では netsh 系機能が存在しないため)。
        // 既定値 null を与えておくことで、Microsoft.Extensions.DependencyInjection は
        // 未登録サービスをエラーにせずここへ null を注入する。
        DnsSettings = dnsSettings;
        NetworkDiagnostics = networkDiagnostics;
        AutoOptimization = autoOptimization;
        TcpTuning = tcpTuning;
        Maintenance = maintenance;

        DnsSettings.PropertyChanged += OnOperationStateChanged;
        AutoOptimization.PropertyChanged += OnOperationStateChanged;
        AutoOptimization.RunQuickOptimizationCommand.PropertyChanged += OnOperationStateChanged;
        NetworkDiagnostics.PropertyChanged += OnOperationStateChanged;
        Version.PropertyChanged += OnOperationStateChanged;
        Version.ClearIgnoredUpdateTagCommand.PropertyChanged += OnOperationStateChanged;
        Version.Initialize();

        DnsSettings.CommandExecuted += OnCommandExecuted;
        AutoOptimization.CommandExecuted += OnCommandExecuted;
        if (TcpTuning is not null)
        {
            TcpTuning.PropertyChanged += OnOperationStateChanged;
            TcpTuning.CommandExecuted += OnCommandExecuted;
            TcpTuning.Initialize(); // 起動時に現在の BBR2 / TCP 設定状態を読み込む
        }

        if (Maintenance is not null)
        {
            Maintenance.CommandExecuted += OnCommandExecuted;
            foreach (var group in Maintenance.Categories)
            {
                group.PropertyChanged += OnOperationStateChanged;
                foreach (var item in group.Items)
                {
                    item.PropertyChanged += OnOperationStateChanged;
                }
            }
        }
    }

    private void OnOperationStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 既存の finally による実行フラグ解除と、コマンドの完了通知へ追従する。
        if (e.PropertyName is null or nameof(DnsSettingsViewModel.IsBusy) or
            nameof(DnsSettingsViewModel.IsPinging) or nameof(DnsSettingsViewModel.IsAdapterNameBusy) or
            nameof(AutoOptimizationViewModel.IsOperationRunning) or nameof(TcpTuningViewModel.IsMtuBusy) or
            nameof(MaintenanceCategoryGroup.IsRunningAll) or
            nameof(VersionViewModel.IsChecking) or nameof(VersionViewModel.IsSavingSettings) or
            nameof(IAsyncRelayCommand.IsRunning))
        {
            OnPropertyChanged(nameof(IsProcessing));
        }

        if (e.PropertyName is null or nameof(DnsSettingsViewModel.OneClickProgressText))
        {
            OnPropertyChanged(nameof(ProcessingText));
        }
    }

    private void OnCommandExecuted(object? sender, CommandExecutionResult result)
    {
        var detail = result.Success ? result.StandardOutput : result.StandardError;
        LogEntries.Insert(0, new CommandLogEntry(DateTime.Now, result.CommandLine, result.Success, detail));

        // 実行時点の記録は executor 側。ここでは適用後確認を含む画面への通知結果を記録する。
        LoggerBootstrap.LogCommandResult(result);

        // ログパネルの肥大化を防ぐため直近 200 件だけ保持する
        while (LogEntries.Count > 200)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }
}
