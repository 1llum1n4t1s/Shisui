using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

public partial class DnsSettingsViewModel : ObservableObject
{
    private readonly INetworkAdapterService _adapterService;
    private readonly IDnsConfigurationService _dnsService;
    private readonly IDnsCacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly INetworkDiagnosticsService _diagnosticsService;
    private readonly IGhostAdapterService? _ghostAdapterService;
    private readonly IDohConfigurationService? _dohService;
    private readonly IDotConfigurationService? _dotService;
    private readonly ITcpTuningService? _tcpTuningService;

    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    public bool IsWindows { get; } = OperatingSystem.IsWindows();

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public ObservableCollection<GhostAdapterItemViewModel> GhostAdapters { get; } = [];

    public IReadOnlyList<DnsProviderPreset> Presets => DnsPresetCatalog.BuiltIn;

    [ObservableProperty]
    private NetworkAdapterInfo? selectedAdapter;

    [ObservableProperty]
    private NetworkAdapterDetails? adapterDetails;

    [ObservableProperty]
    private DnsProviderPreset selectedPreset = DnsPresetCatalog.CloudflareStandard;

    [ObservableProperty]
    private string customIpv4Primary = string.Empty;

    [ObservableProperty]
    private string customIpv4Secondary = string.Empty;

    [ObservableProperty]
    private string customIpv6Primary = string.Empty;

    [ObservableProperty]
    private string customIpv6Secondary = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool isGhostAdaptersBusy;

    [ObservableProperty]
    private string ghostAdaptersStatusText = string.Empty;

    [ObservableProperty]
    private bool useDoh;

    [ObservableProperty]
    private string dohStateText = string.Empty;

    [ObservableProperty]
    private bool useDot;

    [ObservableProperty]
    private bool isPinging;

    [ObservableProperty]
    private string pingStatusText = string.Empty;

    public bool IsCustomPresetSelected => SelectedPreset.Id == DnsPresetCatalog.Custom.Id;

    /// <summary>DoH チェックボックスを表示するか (Windows かつサービス登録済みかつ選択中プリセットが対応)。</summary>
    public bool IsDohAvailable => IsWindows && _dohService is not null && SelectedPreset.DohTemplate is not null;

    /// <summary>DoT チェックボックスを表示するか (Windows かつサービス登録済みかつ選択中プリセットが対応)。</summary>
    public bool IsDotAvailable => IsWindows && _dotService is not null && SelectedPreset.DotHost is not null;

    /// <summary>DoH/DoT 併用時の説明キャプションを表示するか (両方のチェックボックスが表示されているときだけ)。</summary>
    public bool ShowDohDotInteractionNote => IsDohAvailable && IsDotAvailable;

    /// <summary>「おまかせ高速化設定」で BBR2 輻輳制御・受信ウィンドウ自動調整もあわせて行うか (Windows かつサービス登録済みのときだけ)。</summary>
    public bool IsTcpOptimizationAvailable => _tcpTuningService is not null;

    /// <summary>「おまかせ高速化設定」ボタンの説明文。実際に何が行われるかを事前に把握できるようにする。</summary>
    public string OneClickOptimizeDescription => IsTcpOptimizationAvailable
        ? "DNS を Cloudflare に切り替えて暗号化 (DoH) を有効にし、DNS キャッシュをクリアします。あわせて BBR2 輻輳制御を有効化し、受信ウィンドウ自動調整を既定 (Normal) に戻します (この2つは選択中のアダプタに限らず PC 全体に適用されます)。"
        : "DNS を Cloudflare に切り替えて暗号化 (DoH) を有効にし、DNS キャッシュをクリアします。";

    public DnsSettingsViewModel(
        INetworkAdapterService adapterService,
        IDnsConfigurationService dnsService,
        IDnsCacheService cacheService,
        ISettingsService settingsService,
        INetworkDiagnosticsService diagnosticsService,
        IGhostAdapterService? ghostAdapterService = null,
        IDohConfigurationService? dohService = null,
        IDotConfigurationService? dotService = null,
        ITcpTuningService? tcpTuningService = null)
    {
        _adapterService = adapterService;
        _dnsService = dnsService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _diagnosticsService = diagnosticsService;
        _ghostAdapterService = ghostAdapterService;
        _dohService = dohService;
        _dotService = dotService;
        _tcpTuningService = tcpTuningService;

        // 前回選択したプリセットを復元する (組み込みプリセットのみ。フィールド直接代入で OnChanged を
        // 発火させず、初期 DoH 状態の取得は下の RefreshDohStateAsync で一度だけ明示的に行う)。
        var lastPresetId = _settingsService.Current.LastSelectedPresetId;
        if (lastPresetId is not null)
        {
            var restored = Presets.FirstOrDefault(p => p.Id == lastPresetId);
            if (restored is not null)
            {
                selectedPreset = restored;
            }
        }

        // カスタムプリセットの前回入力 IP を復元する (フィールド直接代入、OnChanged 不要)。
        if (selectedPreset.Id == DnsPresetCatalog.Custom.Id)
        {
            customIpv4Primary = _settingsService.Current.CustomIpv4Primary ?? string.Empty;
            customIpv4Secondary = _settingsService.Current.CustomIpv4Secondary ?? string.Empty;
            customIpv6Primary = _settingsService.Current.CustomIpv6Primary ?? string.Empty;
            customIpv6Secondary = _settingsService.Current.CustomIpv6Secondary ?? string.Empty;
        }

        _ = LoadAdaptersAsync();
        if (_ghostAdapterService is not null)
        {
            _ = LoadGhostAdaptersAsync();
        }

        // 起動時に、選択中プリセットの DoH 実状態をチェックボックス・バッジへ反映する
        // (UseDoh は設定ファイルに保存せず、OS の実際の登録状況を単一の真実とする)。
        _ = RefreshDohStateAsync(SelectedPreset.Servers);
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value) => _ = RefreshAdapterDetailsAsync(value);

    private async Task RefreshAdapterDetailsAsync(NetworkAdapterInfo? adapter)
    {
        if (adapter is null)
        {
            AdapterDetails = null;
            return;
        }

        try
        {
            AdapterDetails = await _adapterService.GetAdapterDetailsAsync(adapter.Id);
        }
        catch
        {
            // 詳細情報の取得失敗は致命的ではない (DNS 設定自体は選択・適用できる) ので握りつぶす。
            AdapterDetails = null;
        }
    }

    partial void OnSelectedPresetChanged(DnsProviderPreset value)
    {
        OnPropertyChanged(nameof(IsCustomPresetSelected));
        OnPropertyChanged(nameof(IsDohAvailable));
        OnPropertyChanged(nameof(IsDotAvailable));
        OnPropertyChanged(nameof(ShowDohDotInteractionNote));
        // プリセットを切り替えたら、その宛先の DoH 実状態を取り直してチェックボックス・バッジへ反映する。
        _ = RefreshDohStateAsync(value.Servers);
        // DoT は OS 側に状態読み取り手段が無いため (IDotConfigurationService 参照)、
        // 前のプリセットのチェック状態を持ち越さないよう明示的にリセットする。
        UseDot = false;
        // 前のプリセットに対する疎通テスト結果を持ち越さない。
        PingStatusText = string.Empty;
    }

    [RelayCommand]
    private async Task PingSelectedDnsAsync()
    {
        var servers = ResolveServers();
        var target = servers.Ipv4Primary ?? servers.Ipv6Primary;
        if (target is null)
        {
            PingStatusText = "疎通テストする DNS アドレスがありません";
            return;
        }

        IsPinging = true;
        try
        {
            var result = await _diagnosticsService.PingAsync(target, count: 4);
            PingStatusText = result.Success
                ? $"🟢 応答あり ({target}, 平均 {result.AverageRoundtripMs:F0} ms, {result.Received}/{result.Sent} 件)"
                : $"🔴 応答なし ({target})";
        }
        finally
        {
            IsPinging = false;
        }
    }

    [RelayCommand]
    private async Task LoadAdaptersAsync()
    {
        IsBusy = true;
        try
        {
            var adapters = await _adapterService.GetAdaptersAsync();
            Adapters.Clear();
            foreach (var adapter in adapters)
            {
                Adapters.Add(adapter);
            }

            var lastId = _settingsService.Current.LastSelectedAdapterId;
            SelectedAdapter = (lastId is not null ? Adapters.FirstOrDefault(a => a.Id == lastId) : null)
                               ?? Adapters.FirstOrDefault();
            StatusText = $"{Adapters.Count} 件のアダプタを取得しました";
        }
        catch (Exception ex)
        {
            StatusText = $"アダプタの取得に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedAdapter is null)
        {
            StatusText = "アダプタを選択してください";
            return;
        }

        var servers = ResolveServers();
        if (servers.IsEmpty)
        {
            StatusText = "適用する DNS アドレスがありません";
            return;
        }

        IsBusy = true;
        try
        {
            var results = (await _dnsService.ApplyAsync(SelectedAdapter.Id, servers)).ToList();

            if (IsDohAvailable)
            {
                var dohResults = UseDoh
                    ? await _dohService!.EnableAsync(servers, SelectedPreset.DohTemplate!)
                    : await _dohService!.DisableAsync(servers);
                results.AddRange(dohResults);
            }

            if (IsDotAvailable)
            {
                var dotResults = UseDot
                    ? await _dotService!.EnableAsync(servers, SelectedPreset.DotHost!)
                    : await _dotService!.DisableAsync(servers);
                results.AddRange(dotResults);
            }

            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            _settingsService.Current.LastSelectedAdapterId = SelectedAdapter.Id;
            _settingsService.Current.LastSelectedPresetId = SelectedPreset.Id;
            if (IsCustomPresetSelected)
            {
                _settingsService.Current.CustomIpv4Primary = NullIfEmpty(CustomIpv4Primary);
                _settingsService.Current.CustomIpv4Secondary = NullIfEmpty(CustomIpv4Secondary);
                _settingsService.Current.CustomIpv6Primary = NullIfEmpty(CustomIpv6Primary);
                _settingsService.Current.CustomIpv6Secondary = NullIfEmpty(CustomIpv6Secondary);
            }

            await _settingsService.SaveAsync();

            StatusText = results.All(r => r.Success)
                ? $"{SelectedAdapter.DisplayName} に DNS を適用しました"
                : "一部のコマンドが失敗しました。ログを確認してください";

            await LoadAdaptersAsync();
            await RefreshDohStateAsync(servers);
        }
        finally
        {
            IsBusy = false;
        }
    }


    /// <summary>
    /// PC 初心者でも迷わず使えるように、DNS プリセットの適用・DoH 有効化・DNS キャッシュクリア・
    /// (Windows では) BBR2 輻輳制御の有効化・受信ウィンドウ自動調整の既定化をまとめて行うワンクリック機能。
    /// </summary>
    [RelayCommand]
    private async Task OneClickOptimizeAsync()
    {
        if (SelectedAdapter is null)
        {
            StatusText = "アダプタを選択してください";
            return;
        }

        IsBusy = true;
        try
        {
            // SelectedPreset のセッターではなくバッキングフィールドへ直接代入する。セッター経由だと
            // OnSelectedPresetChanged が同期発火し、その中の fire-and-forget な RefreshDohStateAsync
            // (旧プリセット向けの無駄な呼び出し)が、この後 EnableAsync 実行後に await する
            // RefreshDohStateAsync より後に完了することがあり、UseDoh の表示が古い状態で上書きされる
            // レースになっていた (2026-07-06 /rere レビューで発見)。必要な通知はここで明示的に行い、
            // RefreshDohStateAsync はこのメソッド内で最後に一度だけ (await 済みで) 呼ぶ。
#pragma warning disable MVVMTK0034 // 上記の理由で意図的にセッターを経由せず、通知は次行以降で明示的に行う
            selectedPreset = DnsPresetCatalog.CloudflareStandard;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(IsCustomPresetSelected));
            OnPropertyChanged(nameof(IsDohAvailable));
            OnPropertyChanged(nameof(IsDotAvailable));
            OnPropertyChanged(nameof(ShowDohDotInteractionNote));
            // DoT は DoH と比べて速度上のメリットが無いため (下の解説キャプション参照) ここでは触れないが、
            // 前のプリセットのチェック状態を持ち越さないよう明示的にリセットする (OnSelectedPresetChanged と同じ扱い)。
            UseDot = false;
            PingStatusText = string.Empty;

            var servers = SelectedPreset.Servers;
            var results = new List<CommandExecutionResult>();

            results.AddRange(await _dnsService.ApplyAsync(SelectedAdapter.Id, servers));

            if (IsDohAvailable)
            {
                results.AddRange(await _dohService!.EnableAsync(servers, SelectedPreset.DohTemplate!));
                UseDoh = true;
            }

            results.Add(await _cacheService.FlushAsync());

            if (_tcpTuningService is not null)
            {
                results.AddRange(await _tcpTuningService.EnableBbr2Async());
                results.Add(await _tcpTuningService.SetAutoTuningLevelAsync(AutoTuningLevel.Normal));
            }

            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            _settingsService.Current.LastSelectedAdapterId = SelectedAdapter.Id;
            _settingsService.Current.LastSelectedPresetId = SelectedPreset.Id;
            await _settingsService.SaveAsync();

            StatusText = results.All(r => r.Success)
                ? "おまかせ高速化設定を適用しました"
                : "一部の設定が失敗しました。ログを確認してください";

            await LoadAdaptersAsync();
            await RefreshDohStateAsync(servers);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshDohStateAsync(DnsServerSet servers)
    {
        if (!IsDohAvailable)
        {
            DohStateText = string.Empty;
            UseDoh = false;
            return;
        }

        try
        {
            var status = await _dohService!.GetStatusAsync(servers);
            DohStateText = FormatDohStatus(status);
            // チェックボックスは OS の実状態を反映する (全サーバーで有効なときだけ ON)。
            UseDoh = status == DohStatus.Enabled;
        }
        catch
        {
            // 状態取得の失敗は致命的ではない (適用操作自体は完了している) ので握りつぶし、不明表示にする。
            DohStateText = "⚪ 不明";
        }
    }

    private static string FormatDohStatus(DohStatus status) => status switch
    {
        DohStatus.Enabled => "🟢 有効",
        DohStatus.Disabled => "🔴 無効",
        DohStatus.Partial => "🟡 一部のみ有効",
        _ => "⚪ 不明",
    };

    [RelayCommand]
    private async Task ResetToAutomaticAsync()
    {
        if (SelectedAdapter is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var results = await _dnsService.ResetToAutomaticAsync(SelectedAdapter.Id);
            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            StatusText = $"{SelectedAdapter.DisplayName} を自動取得 (DHCP) に戻しました";
            await LoadAdaptersAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FlushDnsCacheAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _cacheService.FlushAsync();
            CommandExecuted?.Invoke(this, result);
            StatusText = result.Success ? "DNS キャッシュをクリアしました" : "DNS キャッシュのクリアに失敗しました";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadGhostAdaptersAsync()
    {
        if (_ghostAdapterService is null)
        {
            return;
        }

        IsGhostAdaptersBusy = true;
        try
        {
            var ghosts = await _ghostAdapterService.GetGhostAdaptersAsync();
            GhostAdapters.Clear();
            foreach (var ghost in ghosts)
            {
                GhostAdapters.Add(new GhostAdapterItemViewModel(ghost, RemoveGhostAdapterAsync));
            }

            GhostAdaptersStatusText = GhostAdapters.Count == 0
                ? "切断済みのネットワークデバイスは見つかりませんでした"
                : $"{GhostAdapters.Count} 件の切断済みデバイスが見つかりました";
        }
        catch (Exception ex)
        {
            GhostAdaptersStatusText = $"一覧の取得に失敗しました: {ex.Message}";
        }
        finally
        {
            IsGhostAdaptersBusy = false;
        }
    }

    private async Task RemoveGhostAdapterAsync(GhostAdapterItemViewModel item)
    {
        if (_ghostAdapterService is null)
        {
            return;
        }

        item.IsRemoving = true;
        try
        {
            var result = await _ghostAdapterService.RemoveGhostAdapterAsync(item.Info.InstanceId);
            CommandExecuted?.Invoke(this, result);

            if (result.Success)
            {
                GhostAdapters.Remove(item);
                GhostAdaptersStatusText = $"{item.Info.Description} を削除しました";
            }
            else
            {
                GhostAdaptersStatusText = $"{item.Info.Description} の削除に失敗しました。ログを確認してください";
            }
        }
        finally
        {
            item.IsRemoving = false;
        }
    }

    private DnsServerSet ResolveServers() =>
        IsCustomPresetSelected
            ? new DnsServerSet(
                NullIfEmpty(CustomIpv4Primary),
                NullIfEmpty(CustomIpv4Secondary),
                NullIfEmpty(CustomIpv6Primary),
                NullIfEmpty(CustomIpv6Secondary))
            : SelectedPreset.Servers;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
