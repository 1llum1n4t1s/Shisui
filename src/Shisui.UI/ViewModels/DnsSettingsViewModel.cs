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
    private readonly IGhostAdapterService? _ghostAdapterService;
    private readonly IDohConfigurationService? _dohService;

    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    public bool IsWindows { get; } = OperatingSystem.IsWindows();

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public ObservableCollection<GhostAdapterItemViewModel> GhostAdapters { get; } = [];

    public IReadOnlyList<DnsProviderPreset> Presets => DnsPresetCatalog.BuiltIn;

    [ObservableProperty]
    private NetworkAdapterInfo? selectedAdapter;

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

    public bool IsCustomPresetSelected => SelectedPreset.Id == DnsPresetCatalog.Custom.Id;

    /// <summary>DoH チェックボックスを表示するか (Windows かつサービス登録済みかつ選択中プリセットが対応)。</summary>
    public bool IsDohAvailable => IsWindows && _dohService is not null && SelectedPreset.DohTemplate is not null;

    public DnsSettingsViewModel(
        INetworkAdapterService adapterService,
        IDnsConfigurationService dnsService,
        IDnsCacheService cacheService,
        ISettingsService settingsService,
        IGhostAdapterService? ghostAdapterService = null,
        IDohConfigurationService? dohService = null)
    {
        _adapterService = adapterService;
        _dnsService = dnsService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _ghostAdapterService = ghostAdapterService;
        _dohService = dohService;

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

        _ = LoadAdaptersAsync();
        if (_ghostAdapterService is not null)
        {
            _ = LoadGhostAdaptersAsync();
        }

        // 起動時に、選択中プリセットの DoH 実状態をチェックボックス・バッジへ反映する
        // (UseDoh は設定ファイルに保存せず、OS の実際の登録状況を単一の真実とする)。
        _ = RefreshDohStateAsync(SelectedPreset.Servers);
    }

    partial void OnSelectedPresetChanged(DnsProviderPreset value)
    {
        OnPropertyChanged(nameof(IsCustomPresetSelected));
        OnPropertyChanged(nameof(IsDohAvailable));
        // プリセットを切り替えたら、その宛先の DoH 実状態を取り直してチェックボックス・バッジへ反映する。
        _ = RefreshDohStateAsync(value.Servers);
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

            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            _settingsService.Current.LastSelectedAdapterId = SelectedAdapter.Id;
            _settingsService.Current.LastSelectedPresetId = SelectedPreset.Id;
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
