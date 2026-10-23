using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.UI.ViewModels;

public partial class DnsSettingsViewModel : ObservableObject
{
    private const string RouteClearCommandId = "route-clear";

    private readonly INetworkAdapterService _adapterService;
    private readonly IDnsConfigurationService _dnsService;
    private readonly IDnsCacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly INetworkDiagnosticsService _diagnosticsService;
    private readonly INetworkMutationGate _networkMutationGate;
    private readonly INetworkAdapterNameService? _adapterNameService;
    private readonly IDohConfigurationService? _dohService;
    private readonly IDotConfigurationService? _dotService;
    private readonly ITcpTuningService? _tcpTuningService;
    private readonly INetworkMaintenanceService? _maintenanceService;
    private readonly IDhcpLeaseService? _dhcpLeaseService;
    private readonly IGamingNetworkProfileService? _gamingNetworkProfileService;
    private readonly IWinsockResetService? _winsockResetService;
    private readonly IWinHttpCacheService? _winHttpCacheService;
    private readonly IIpStackResetService? _ipStackResetService;
    private int _adapterDetailsRequestVersion;
    private int _dohStateRequestVersion;
    private int _busyOperationCount;

    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    public bool IsWindows { get; } = OperatingSystem.IsWindows();

    public bool IsAdapterNameCleanupAvailable => IsWindows && _adapterNameService is not null;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public IReadOnlyList<DnsProviderPreset> Presets => DnsPresetCatalog.BuiltIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentDnsReadFailed))]
    [NotifyPropertyChangedFor(nameof(CurrentIpv4Dns))]
    [NotifyPropertyChangedFor(nameof(CurrentIpv6Dns))]
    private NetworkAdapterInfo? selectedAdapter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMacAddress))]
    [NotifyPropertyChangedFor(nameof(CurrentLinkSpeedText))]
    private NetworkAdapterDetails? adapterDetails;

    public bool IsCurrentDnsReadFailed => SelectedAdapter?.DnsReadFailed ?? false;

    public IReadOnlyList<string> CurrentIpv4Dns => SelectedAdapter?.CurrentIPv4Dns ?? [];

    public IReadOnlyList<string> CurrentIpv6Dns => SelectedAdapter?.CurrentIPv6Dns ?? [];

    public string CurrentMacAddress => AdapterDetails?.MacAddress ?? "取得できません";

    public string CurrentLinkSpeedText => AdapterDetails?.LinkSpeedText ?? "取得できません";

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
    private string oneClickProgressText = string.Empty;

    [ObservableProperty]
    private bool isAdapterNameBusy;

    [ObservableProperty]
    private string adapterNameStatusText = string.Empty;

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

    /// <summary>「おまかせ高速化設定」で BBR2 輻輳制御を有効化し、TCP 詳細設定を整えるか (Windows かつサービス登録済みのときだけ)。</summary>
    public bool IsTcpOptimizationAvailable => _tcpTuningService is not null;

    /// <summary>「おまかせ高速化設定」で NetBIOS 名前・ARP・経路キャッシュもあわせてクリアするか (Windows かつサービス登録済みのときだけ)。</summary>
    public bool IsCacheMaintenanceAvailable => _maintenanceService is not null;

    private bool IsRouteCleanupAvailable => _maintenanceService?.GetAvailableCommands()
        .Any(command => command.Id == RouteClearCommandId && command.IncludeInOneClickOptimization) == true;

    /// <summary>一括適用する設定を、項目と変更内容だけで示す。</summary>
    public string OneClickOptimizeDescription
    {
        get
        {
            var items = new List<string> { "DNS：Cloudflare", "DNS キャッシュ：削除" };
            if (IsWindows && _dohService is not null)
                items.Add("DoH：ON");
            if (_tcpTuningService is not null)
            {
                items.Add("TCP 設定・旧レジストリ調整：初期化後に再設定");
                items.Add("輻輳制御：BBR2 / 受信ウィンドウ：Normal");
                items.Add("TCP RACK・TLP・RSS：ON / RSC：OFF");
                items.Add("ECN・timestamps・Fast Open / fallback：ON");
            }
            if (_gamingNetworkProfileService is not null)
            {
                items.Add("NIC 割り込み調整・省電力：OFF（対応項目）");
                items.Add("NIC RSS・LSO・チェックサム：ON（対応項目）");
                items.Add("Realtek PCIe Flow Control・Advanced EEE：OFF（対応項目）");
                items.Add("NIC URO：OFF（対応項目）");
            }
            if (_ipStackResetService is not null)
                items.Add("IP スタック：IPv4・IPv6 をリセットしてから下記の設定を適用");
            if (_maintenanceService is not null)
            {
                items.Add("IPsec 通信状態：削除（接続がある場合）");
                items.Add("WINS 名前登録：更新（利用時）");
                items.Add("NetBIOS・ARP・経路・IPv6 近隣キャッシュ：削除");
                items.Add("Winsock 送信バッファ自動調整：ON");
                items.Add("UDP URO・USO（OS）：既定化");
                items.Add("PCI Express 省電力：OFF（電源接続時）");
                items.Add("Wi-Fi 電源：最大パフォーマンス（電源接続時）");
            }
            if (_dhcpLeaseService is not null)
                items.Add("DHCP アドレス：再取得");
            if (_winsockResetService is not null)
                items.Add("Winsock：初期化（カスタム LSP がない場合）");
            if (_winHttpCacheService is not null)
                items.Add("WinHTTP 自動検出キャッシュ：削除");
            if (_adapterNameService is not null)
                items.Add("切断済み NIC 登録：削除 / 接続名：連番整理");
            if (IsRouteCleanupAvailable)
                items.Add("ルーティングテーブル：削除");
            return string.Join(Environment.NewLine, items.Select(item => "• " + item));
        }
    }

    public DnsSettingsViewModel(
        INetworkAdapterService adapterService,
        IDnsConfigurationService dnsService,
        IDnsCacheService cacheService,
        ISettingsService settingsService,
        INetworkDiagnosticsService diagnosticsService,
        INetworkMutationGate networkMutationGate,
        IDohConfigurationService? dohService = null,
        IDotConfigurationService? dotService = null,
        ITcpTuningService? tcpTuningService = null,
        INetworkMaintenanceService? maintenanceService = null,
        INetworkAdapterNameService? adapterNameService = null,
        IDhcpLeaseService? dhcpLeaseService = null,
        IGamingNetworkProfileService? gamingNetworkProfileService = null,
        IWinsockResetService? winsockResetService = null,
        IWinHttpCacheService? winHttpCacheService = null,
        IIpStackResetService? ipStackResetService = null)
    {
        _adapterService = adapterService;
        _dnsService = dnsService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _diagnosticsService = diagnosticsService;
        _networkMutationGate = networkMutationGate;
        _adapterNameService = adapterNameService;
        _dohService = dohService;
        _dotService = dotService;
        _tcpTuningService = tcpTuningService;
        _maintenanceService = maintenanceService;
        _dhcpLeaseService = dhcpLeaseService;
        _gamingNetworkProfileService = gamingNetworkProfileService;
        _winsockResetService = winsockResetService;
        _winHttpCacheService = winHttpCacheService;
        _ipStackResetService = ipStackResetService;

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

        // 起動時に、選択中プリセットの DoH 実状態をチェックボックス・バッジへ反映する
        // (UseDoh は設定ファイルに保存せず、OS の実際の登録状況を単一の真実とする)。
        _ = RefreshDohStateAsync(SelectedPreset.Servers);
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value) => _ = RefreshAdapterDetailsAsync(value);

    private async Task RefreshAdapterDetailsAsync(NetworkAdapterInfo? adapter)
    {
        var requestVersion = Interlocked.Increment(ref _adapterDetailsRequestVersion);
        if (adapter is null)
        {
            if (requestVersion == Volatile.Read(ref _adapterDetailsRequestVersion))
            {
                AdapterDetails = null;
            }
            return;
        }

        try
        {
            var details = await _adapterService.GetAdapterDetailsAsync(adapter.Id);
            if (requestVersion == Volatile.Read(ref _adapterDetailsRequestVersion))
            {
                AdapterDetails = details;
            }
        }
        catch
        {
            // 詳細情報の取得失敗は致命的ではない (DNS 設定自体は選択・適用できる) ので握りつぶす。
            if (requestVersion == Volatile.Read(ref _adapterDetailsRequestVersion))
            {
                AdapterDetails = null;
            }
        }
    }

    partial void OnSelectedPresetChanged(DnsProviderPreset value)
    {
        NotifySelectedPresetDependentPropertiesChanged();
        // プリセットを切り替えたら、その宛先の DoH 実状態を取り直してチェックボックス・バッジへ反映する。
        _ = RefreshDohStateAsync(value.Servers);
        // DoT は OS 側に状態読み取り手段が無いため (IDotConfigurationService 参照)、
        // 前のプリセットのチェック状態を持ち越さないよう明示的にリセットする。
        UseDot = false;
        // 前のプリセットに対する疎通テスト結果を持ち越さない。
        PingStatusText = string.Empty;
    }

    private void NotifySelectedPresetDependentPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsCustomPresetSelected));
        OnPropertyChanged(nameof(IsDohAvailable));
        OnPropertyChanged(nameof(IsDotAvailable));
        OnPropertyChanged(nameof(ShowDohDotInteractionNote));
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
        BeginBusyOperation();
        try
        {
            await LoadAdaptersCoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"アダプタの取得に失敗しました: {ex.Message}";
        }
        finally
        {
            EndBusyOperation();
        }
    }

    private async Task LoadAdaptersCoreAsync()
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

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedAdapter is null)
        {
            StatusText = "アダプタを選択してください";
            return;
        }

        // await をまたぐ操作では、UIの選択状態を一度だけ読み取って固定する。
        var preset = SelectedPreset;
        var customIpv4Primary = NullIfEmpty(CustomIpv4Primary);
        var customIpv4Secondary = NullIfEmpty(CustomIpv4Secondary);
        var customIpv6Primary = NullIfEmpty(CustomIpv6Primary);
        var customIpv6Secondary = NullIfEmpty(CustomIpv6Secondary);
        var isCustomPreset = preset.Id == DnsPresetCatalog.Custom.Id;
        var servers = isCustomPreset
            ? new DnsServerSet(customIpv4Primary, customIpv4Secondary, customIpv6Primary, customIpv6Secondary)
            : preset.Servers;
        var dohAvailable = IsWindows && _dohService is not null && preset.DohTemplate is not null;
        var useDoh = UseDoh;
        var dotAvailable = IsWindows && _dotService is not null && preset.DotHost is not null;
        var useDot = UseDot;
        if (!servers.HasValidAddressFamilies)
        {
            StatusText = "IPv4 / IPv6 の DNS アドレスを正しい形式で入力してください";
            return;
        }

        if (servers.IsEmpty)
        {
            StatusText = "適用する DNS アドレスがありません";
            return;
        }

        BeginBusyOperation();
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var adapter = SelectedAdapter;
            if (adapter is null)
            {
                StatusText = "アダプタを選択してください";
                return;
            }

            var results = (await _dnsService.ApplyAsync(adapter.Id, servers)).ToList();

            if (dohAvailable)
            {
                var dohResults = useDoh
                    ? await _dohService!.EnableAsync(servers, preset.DohTemplate!)
                    : await _dohService!.DisableAsync(servers);
                results.AddRange(dohResults);
            }

            if (dotAvailable)
            {
                var dotResults = useDot
                    ? await _dotService!.EnableAsync(servers, preset.DotHost!)
                    : await _dotService!.DisableAsync(servers);
                results.AddRange(dotResults);
            }

            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            _settingsService.Current.LastSelectedAdapterId = adapter.Id;
            _settingsService.Current.LastSelectedPresetId = preset.Id;
            if (isCustomPreset)
            {
                _settingsService.Current.CustomIpv4Primary = customIpv4Primary;
                _settingsService.Current.CustomIpv4Secondary = customIpv4Secondary;
                _settingsService.Current.CustomIpv6Primary = customIpv6Primary;
                _settingsService.Current.CustomIpv6Secondary = customIpv6Secondary;
            }

            var settingsSaved = await TrySaveSelectionAsync();

            await LoadAdaptersCoreAsync();
            if (SelectedPreset.Id == preset.Id)
            {
                await RefreshDohStateAsync(servers);
            }

            StatusText = results.All(r => r.Success)
                ? $"{adapter.DisplayName} に DNS を適用しました"
                : "一部のコマンドが失敗しました。ログを確認してください";
            if (!settingsSaved)
            {
                StatusText += " アプリの設定を保存できませんでした。ログを確認してください";
            }
        }
        catch (Exception ex)
        {
            StatusText = "DNS の適用または現在値の確認に失敗しました。ログを確認してください";
            LoggerBootstrap.Log.Error("DNS の適用または現在値の確認に失敗しました", ex);
        }
        finally
        {
            EndBusyOperation();
        }
    }


    /// <summary>
    /// PC 初心者でも迷わず使えるように、DNS プリセットの適用・DoH 有効化・DNS キャッシュクリア・
    /// (Windows では) NetBIOS 名前/ARP・経路キャッシュの追加クリア・TCP 詳細設定の既定化・
    /// BBR2 輻輳制御の有効化・受信ウィンドウ自動調整の既定化・ゲーム向け NIC 設定・切断済みアダプタ登録の全削除を
    /// まとめて行うワンクリック機能。
    /// </summary>
    internal async Task RunOneClickOptimizationAsync()
    {
        if (SelectedAdapter is null)
        {
            StatusText = "アダプタを選択してください";
            return;
        }

        using var diagnosticOperation = LoggerBootstrap.BeginOperation("おまかせ高速化設定", SelectedAdapter.Id);
        var results = new List<CommandExecutionResult>();
        var resultsReported = false;
        var routeCleanupAttempted = false;
        var winsockRepairRequested = false;
        var ipStackResetRequested = false;
        OneClickProgressText = "適用の準備をしています";
        BeginBusyOperation();
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var adapter = SelectedAdapter;
            if (adapter is null)
            {
                StatusText = "アダプタを選択してください";
                return;
            }

            if (_ipStackResetService is not null)
            {
                OneClickProgressText = "IPv4・IPv6 の IP スタックをリセットしています";
                ipStackResetRequested = true;
                var resetResults = await _ipStackResetService.ResetAsync();
                results.AddRange(resetResults);
                if (resetResults.Any(r => r.RequiresOperationAbort))
                    throw new InvalidOperationException("IP 初期化の終了を確認できないため、後続の設定を中断しました。ログを確認してください。");
            }

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
            NotifySelectedPresetDependentPropertiesChanged();
            // DoT は DoH と比べて速度上のメリットが無いため (下の解説キャプション参照) ここでは触れないが、
            // 前のプリセットのチェック状態を持ち越さないよう明示的にリセットする (OnSelectedPresetChanged と同じ扱い)。
            UseDot = false;
            PingStatusText = string.Empty;

            var preset = DnsPresetCatalog.CloudflareStandard;
            var servers = preset.Servers;
            NetworkAdapterNameCleanupResult? adapterNameCleanupResult = null;
            MaintenanceCommandDefinition? routeClearCommand = null;
            var oneClickMaintenanceCommands = _maintenanceService?.GetAvailableCommands()
                .Where(c => c.IncludeInOneClickOptimization).ToArray() ?? [];

            // 接続状態を破棄する修復は、DHCP と希望の通信設定を適用する前に済ませる。
            foreach (var command in oneClickMaintenanceCommands.Where(c =>
                         c.RunBeforeOneClickConfiguration && c.Id != RouteClearCommandId))
            {
                OneClickProgressText = command.Label + "…";
                CommandExecutionResult repairResult;
                try
                {
                    repairResult = await _maintenanceService!.RunAsync(command.Id);
                }
                catch (Exception ex)
                {
                    repairResult = new(false, command.Label, -1, string.Empty, ex.Message);
                    LoggerBootstrap.Log.Error($"おまかせ高速化設定の事前修復に失敗しました: {command.Id}", ex);
                }
                results.Add(repairResult);
                if (repairResult.RequiresOperationAbort)
                    throw new InvalidOperationException(repairResult.StandardError);
            }

            // 先に条件付き修復を済ませ、Winsock 自動調整や希望の TCP/NIC 設定を後から適用する。
            if (_winsockResetService is not null)
            {
                OneClickProgressText = "Winsock を確認・修復しています";
                winsockRepairRequested = true;
                try
                {
                    results.Add(await _winsockResetService.ResetIfNoLayeredProvidersAsync());
                }
                catch (Exception ex)
                {
                    results.Add(new(false, "Winsock の条件付き修復", -1, string.Empty, ex.Message));
                    LoggerBootstrap.Log.Error("おまかせ高速化設定の Winsock 修復に失敗しました", ex);
                }
            }
            if (_winHttpCacheService is not null)
            {
                try
                {
                    OneClickProgressText = "WinHTTP キャッシュを削除しています";
                    results.Add(await _winHttpCacheService.ClearAutoProxyCacheAsync());
                }
                catch (Exception ex)
                {
                    results.Add(new(false, "WinHTTP 自動検出キャッシュの消去", -1, string.Empty, ex.Message));
                    LoggerBootstrap.Log.Error("おまかせ高速化設定の WinHTTP キャッシュ消去に失敗しました", ex);
                }
            }

            if (_dhcpLeaseService is not null)
            {
                // DHCP 再取得後に希望の DNS を適用する。解放・再取得は選択した NIC だけを対象にする。
                OneClickProgressText = "IP アドレスを再取得しています";
                results.AddRange(await _dhcpLeaseService.ReacquireAsync(adapter.Id));
            }

            OneClickProgressText = "DNS・DoH を設定しています";
            results.AddRange(await _dnsService.ApplyAsync(adapter.Id, servers));

            if (IsDohAvailable)
            {
                results.AddRange(await _dohService!.EnableAsync(servers, preset.DohTemplate!));
                UseDoh = true;
            }

            OneClickProgressText = "通信キャッシュを削除しています";
            results.Add(await _cacheService.FlushAsync());

            if (_maintenanceService is not null)
            {
                // カタログでワンクリック対象と明示されたメンテナンスだけを実行する。許可リスト方式にすることで、
                // 手動メンテナンス用コマンドを今後同じカテゴリへ追加しても、意図せず初心者向けボタンへ混入しない。
                // DNS キャッシュは上の _cacheService.FlushAsync() で既に処理済み。WINS 再登録は構成がある場合だけ行う。
                // DNS 再登録と HTTP.sys のログ/応答キャッシュは対象外。送信自動調整は受信側の
                // Auto-Tuning とは別系統なので、古い高速化ツールで無効化された状態をここで明示的に有効へ戻す。
                foreach (var command in oneClickMaintenanceCommands)
                {
                    if (command.Id == RouteClearCommandId)
                    {
                        // PC 全体の通信が切れる経路消去は、ほかの設定と接続名整理を終えた最後に実行する。
                        routeClearCommand = command;
                        continue;
                    }
                    if (command.RunBeforeOneClickConfiguration)
                        continue;
                    OneClickProgressText = command.Label + "…";
                    var maintenanceResult = await _maintenanceService.RunAsync(command.Id);
                    results.Add(maintenanceResult);
                    if (maintenanceResult.RequiresOperationAbort)
                        throw new InvalidOperationException(maintenanceResult.StandardError);
                }
            }

            if (_tcpTuningService is not null)
            {
                // まず公式の TCP 全体リセットで、他のチューニングツールが変更した supplemental template や
                // フィルターを含むユーザー構成を削除する。その後の個別コマンドは、全体リセットが一部失敗した
                // 環境でのフォールバックと、実行ログ上で各項目の成否を確認できるようにするため意図的に重ねる。
                // 全体リセット後に BBR2 を設定することで、既定化の対象に巻き込まれず最終状態を BBR2 にする。
                OneClickProgressText = "TCP の低遅延設定を適用しています";
                results.Add(await _tcpTuningService.ResetAllTcpSettingsToDefaultAsync());
                results.AddRange(await _tcpTuningService.EnableBbr2Async());
                results.AddRange(await _tcpTuningService.RevertGlobalOptionsToDefaultAsync());
                // 修復用の既定化とは別に、対応経路での輻輳通知・RTT 計測・接続開始の短縮を利用可能にする。
                results.AddRange(await _tcpTuningService.ApplyLowLatencyGlobalOptionsAsync());
                results.Add(await _tcpTuningService.RevertLegacyTcpRegistryTweaksToDefaultAsync());
                results.Add(await _tcpTuningService.SetAutoTuningLevelAsync(AutoTuningLevel.Normal));
                results.AddRange(await _tcpTuningService.EnableLossRecoveryAsync());

                // 設定コマンドが成功しても、非対応ビルドやポリシー上書きで目的の実状態とは限らない。
                // 読み取り失敗は結果へ残し、完了した設定の保存と接続名整理は継続する。
                try
                {
                    OneClickProgressText = "TCP 設定を確認しています";
                    var snapshot = await _tcpTuningService.GetCurrentStateAsync();
                    results.Add(TcpSettingsVerifier.VerifyBbr2Enabled(snapshot));
                    results.Add(TcpSettingsVerifier.VerifyAutoTuning(snapshot, AutoTuningLevel.Normal));
                    results.Add(TcpSettingsVerifier.VerifyGlobalOption(snapshot, TcpGlobalOption.Rss, true));
                    results.Add(TcpSettingsVerifier.VerifyGlobalOption(snapshot, TcpGlobalOption.Rsc, false));
                    results.Add(TcpSettingsVerifier.VerifyGlobalOption(snapshot, TcpGlobalOption.EcnCapability, true));
                    results.Add(TcpSettingsVerifier.VerifyGlobalOption(snapshot, TcpGlobalOption.Timestamps, true));
                    results.Add(TcpSettingsVerifier.VerifyGlobalOption(snapshot, TcpGlobalOption.FastOpen, true));
                }
                catch (Exception ex)
                {
                    results.Add(new(false, "TCP 適用後確認", -1, string.Empty,
                        $"設定の実状態を確認できませんでした: {ex.Message}"));
                }
            }

            if (_gamingNetworkProfileService is not null)
            {
                // 一括処理が保持する同じリースで適用し、ゲートの二重取得と改名前の接続名の取り違えを防ぐ。
                try
                {
                    OneClickProgressText = "NIC の低遅延設定を適用しています";
                    var gamingResults = await _gamingNetworkProfileService.ApplyWithinMutationLeaseAsync(adapter.Id);
                    results.AddRange(gamingResults);
                    if (gamingResults.Count == 0)
                    {
                        results.Add(new(false, "ゲーム向け NIC 設定: 結果確認", -1, string.Empty,
                            "ゲーム向け NIC 設定の変更結果を取得できませんでした。"));
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new(false, "ゲーム向け NIC 設定: 適用失敗", -1, string.Empty,
                        $"ゲーム向け NIC 設定を完了できませんでした: {ex.Message}"));
                    LoggerBootstrap.Log.Error("おまかせ高速化設定の NIC 設定に失敗しました", ex);
                }
            }

            if (_adapterNameService is not null)
            {
                // Windows では adapter.Id が接続名そのものなので、名前を使う DNS/TCP/NIC 処理をすべて終えてから
                // 切断済み登録を削除し、必要なら選択中アダプタの末尾連番を外す。
                try
                {
                    OneClickProgressText = "切断済み NIC 登録・接続名を整理しています";
                    adapterNameCleanupResult = await _adapterNameService.CleanupAsync(adapter.DisplayName);
                    results.AddRange(adapterNameCleanupResult.CommandResults);
                    AdapterNameStatusText = FormatAdapterNameCleanupStatus(adapterNameCleanupResult);
                    if (adapterNameCleanupResult.WasRenamed)
                    {
                        // 後続の一覧再取得が失敗しても、改名前の ID を次の変更操作へ渡さない。
                        SelectedAdapter = null;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new CommandExecutionResult(false, "接続名の整理", -1, string.Empty, ex.Message));
                    AdapterNameStatusText = $"接続名の整理に失敗しました: {ex.Message}";
                    LoggerBootstrap.Log.Error("おまかせ高速化設定の接続名整理に失敗しました", ex);
                }
            }

            if (routeClearCommand is not null)
            {
                OneClickProgressText = "ルーティングテーブルを削除しています";
                routeCleanupAttempted = true;
                try
                {
                    results.Add(await _maintenanceService!.RunAsync(routeClearCommand.Id));
                }
                catch (Exception ex)
                {
                    results.Add(new(false, "route /f", -1, string.Empty, ex.Message));
                    LoggerBootstrap.Log.Error("おまかせ高速化設定の経路消去に失敗しました", ex);
                }
            }

            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }
            resultsReported = true;

            _settingsService.Current.LastSelectedAdapterId = adapterNameCleanupResult is
                { WasRenamed: true, TargetName: not null }
                ? adapterNameCleanupResult.TargetName
                : adapter.Id;
            _settingsService.Current.LastSelectedPresetId = preset.Id;
            OneClickProgressText = "結果を保存・確認しています";
            var settingsSaved = await TrySaveSelectionAsync();

            var allSucceeded = results.All(r => r.Success)
                               && (adapterNameCleanupResult?.Success ?? true);

            await LoadAdaptersCoreAsync();
            await RefreshDohStateAsync(servers);

            if (allSucceeded)
            {
                StatusText = IsTcpOptimizationAvailable
                    ? "設定コマンドを実行しました。BBR2・RSS・Fast Open 有効 / RSC 無効と、Internet の ECN・timestamps 有効・受信ウィンドウ実効 Normal を確認済みです。Fast Open fallback・RACK / TLP・UDP の実状態は未検証です。遅延改善量も未測定です。"
                    : "おまかせ高速化設定を適用しました";
                if (_gamingNetworkProfileService is not null)
                {
                    StatusText += " ゲーム向け NIC 設定の保存値も確認済みです。";
                }
            }
            else
            {
                StatusText = "一部の設定が失敗、または適用後の状態を確認できませんでした。ログを確認してください";
            }
            if (ipStackResetRequested || routeCleanupAttempted || winsockRepairRequested
                || (allSucceeded && (IsTcpOptimizationAvailable || _gamingNetworkProfileService is not null)))
            {
                StatusText += " 設定の反映には PC を再起動してください";
            }
            if (!settingsSaved)
            {
                StatusText += " アプリの設定を保存できませんでした。ログを確認してください";
            }
        }
        catch (Exception ex)
        {
            if (!resultsReported)
            {
                foreach (var result in results)
                {
                    CommandExecuted?.Invoke(this, result);
                }
            }

            StatusText = $"おまかせ高速化設定に失敗しました: {ex.Message}";
            if (ipStackResetRequested || routeCleanupAttempted || winsockRepairRequested)
            {
                StatusText += " PC を再起動してください";
            }
            LoggerBootstrap.Log.Error("おまかせ高速化設定に失敗しました", ex);
        }
        finally
        {
            OneClickProgressText = string.Empty;
            EndBusyOperation();
        }
    }

    private async Task<bool> TrySaveSelectionAsync()
    {
        try
        {
            await _settingsService.SaveAsync();
            return true;
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("DNS 選択設定を保存できませんでした", ex);
            return false;
        }
    }

    private async Task RefreshDohStateAsync(DnsServerSet servers)
    {
        var requestVersion = Interlocked.Increment(ref _dohStateRequestVersion);
        if (!IsDohAvailable)
        {
            if (requestVersion == Volatile.Read(ref _dohStateRequestVersion))
            {
                DohStateText = string.Empty;
                UseDoh = false;
            }
            return;
        }

        try
        {
            var status = await _dohService!.GetStatusAsync(servers);
            if (requestVersion == Volatile.Read(ref _dohStateRequestVersion))
            {
                DohStateText = FormatDohStatus(status);
                // チェックボックスは OS の実状態を反映する (全サーバーで有効なときだけ ON)。
                UseDoh = status == DohStatus.Enabled;
            }
        }
        catch
        {
            // 状態取得の失敗は致命的ではない (適用操作自体は完了している) ので握りつぶし、不明表示にする。
            if (requestVersion == Volatile.Read(ref _dohStateRequestVersion))
            {
                DohStateText = "⚪ 不明";
            }
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

        BeginBusyOperation();
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var adapter = SelectedAdapter;
            if (adapter is null)
            {
                return;
            }

            var results = await _dnsService.ResetToAutomaticAsync(adapter.Id);
            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            var allSucceeded = results.All(result => result.Success);
            await LoadAdaptersCoreAsync();
            StatusText = allSucceeded
                ? $"{adapter.DisplayName} を自動取得 (DHCP) に戻しました"
                : "DNS を自動取得 (DHCP) に戻せませんでした。ログを確認してください";
        }
        finally
        {
            EndBusyOperation();
        }
    }

    [RelayCommand]
    private async Task FlushDnsCacheAsync()
    {
        BeginBusyOperation();
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var result = await _cacheService.FlushAsync();
            CommandExecuted?.Invoke(this, result);
            StatusText = result.Success ? "DNS キャッシュをクリアしました" : "DNS キャッシュのクリアに失敗しました";
        }
        finally
        {
            EndBusyOperation();
        }
    }

    [RelayCommand]
    private async Task CleanupAdapterNameAsync()
    {
        if (_adapterNameService is null)
        {
            return;
        }

        IsAdapterNameBusy = true;
        BeginBusyOperation();
        try
        {
            NetworkAdapterNameCleanupResult result;
            using (var mutationLease = await _networkMutationGate.EnterAsync())
            {
                var adapterName = SelectedAdapter?.DisplayName;
                result = await _adapterNameService.CleanupAsync(adapterName);

                if (result is { WasRenamed: true, TargetName: not null })
                {
                    // 改名前を参照する待機中の変更操作へゲートを渡す前に、選択中アダプタを新しい名前へ更新する。
                    await ReloadAdaptersAfterNameChangeAsync(result.TargetName);
                }
            }

            foreach (var commandResult in result.CommandResults)
            {
                CommandExecuted?.Invoke(this, commandResult);
            }

            AdapterNameStatusText = FormatAdapterNameCleanupStatus(result);
        }
        catch (Exception ex)
        {
            AdapterNameStatusText = $"接続名の整理に失敗しました: {ex.Message}";
        }
        finally
        {
            EndBusyOperation();
            IsAdapterNameBusy = false;
        }
    }

    private void BeginBusyOperation()
    {
        if (Interlocked.Increment(ref _busyOperationCount) == 1)
        {
            IsBusy = true;
        }
    }

    private void EndBusyOperation()
    {
        if (Interlocked.Decrement(ref _busyOperationCount) == 0)
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAdaptersAfterNameChangeAsync(string preferredName)
    {
        // 再取得に失敗した場合も、既に無効になった改名前の ID を選択状態へ残さない。
        SelectedAdapter = null;
        var adapters = await _adapterService.GetAdaptersAsync();
        Adapters.Clear();
        foreach (var adapter in adapters)
        {
            Adapters.Add(adapter);
        }

        SelectedAdapter = Adapters.FirstOrDefault(adapter =>
                              string.Equals(adapter.DisplayName, preferredName, StringComparison.OrdinalIgnoreCase))
                          ?? Adapters.FirstOrDefault();
    }

    private static string FormatAdapterNameCleanupStatus(NetworkAdapterNameCleanupResult result)
    {
        if (!result.Success)
        {
            var completed = result.RemovedGhostCount > 0
                ? $" (切断済みの旧デバイス {result.RemovedGhostCount} 件は削除済み)"
                : string.Empty;
            return $"{result.ErrorMessage ?? "接続名を整理できませんでした"}{completed}";
        }

        if (result.TargetName is null)
        {
            return result.RemovedGhostCount > 0
                ? $"切断済みのネットワークデバイス登録 {result.RemovedGhostCount} 件をすべて削除しました"
                : "切断済みのネットワークデバイス登録は見つかりませんでした";
        }

        if (result.WasRenamed)
        {
            return $"切断済みの旧デバイス {result.RemovedGhostCount} 件を整理し、接続名を「{result.TargetName}」に変更しました";
        }

        return result.RemovedGhostCount > 0
            ? $"切断済みの旧デバイス {result.RemovedGhostCount} 件を整理しました。接続名はすでに「{result.TargetName}」です"
            : $"整理対象の旧デバイスはありません。接続名はすでに「{result.TargetName}」です";
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
