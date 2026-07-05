using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public bool IsWindows { get; } = OperatingSystem.IsWindows();

    public bool IsMacOS { get; } = OperatingSystem.IsMacOS();

    public DnsSettingsViewModel DnsSettings { get; }

    /// <summary>Ping / トレースルート診断タブ (Windows/macOS 両対応、常に非 null)。</summary>
    public NetworkDiagnosticsViewModel NetworkDiagnostics { get; }

    /// <summary>Windows でのみ非 null。View 側は null を「タブ非表示」の合図として扱う。</summary>
    public TcpTuningViewModel? TcpTuning { get; }

    /// <summary>Windows でのみ非 null。View 側は null を「タブ非表示」の合図として扱う。</summary>
    public MaintenanceViewModel? Maintenance { get; }

    public VersionViewModel Version { get; }

    public ObservableCollection<CommandLogEntry> LogEntries { get; } = [];

    public MainWindowViewModel(
        DnsSettingsViewModel dnsSettings,
        NetworkDiagnosticsViewModel networkDiagnostics,
        VersionViewModel version,
        TcpTuningViewModel? tcpTuning = null,
        MaintenanceViewModel? maintenance = null)
    {
        Version = version;
        Version.Initialize();

        // TcpTuning / Maintenance は Windows でしか DI 登録されない (macOS では netsh 系機能が存在しないため)。
        // 既定値 null を与えておくことで、Microsoft.Extensions.DependencyInjection は
        // 未登録サービスをエラーにせずここへ null を注入する。
        DnsSettings = dnsSettings;
        NetworkDiagnostics = networkDiagnostics;
        TcpTuning = tcpTuning;
        Maintenance = maintenance;

        DnsSettings.CommandExecuted += OnCommandExecuted;
        if (TcpTuning is not null)
        {
            TcpTuning.CommandExecuted += OnCommandExecuted;
            TcpTuning.Initialize(); // 起動時に現在の BBR2 / TCP 設定状態を読み込む
        }

        if (Maintenance is not null)
        {
            Maintenance.CommandExecuted += OnCommandExecuted;
        }
    }

    private void OnCommandExecuted(object? sender, CommandExecutionResult result)
    {
        var detail = result.Success ? result.StandardOutput : result.StandardError;
        LogEntries.Insert(0, new CommandLogEntry(DateTime.Now, result.CommandLine, result.Success, detail));

        // ログパネルの肥大化を防ぐため直近 200 件だけ保持する
        while (LogEntries.Count > 200)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }
}
