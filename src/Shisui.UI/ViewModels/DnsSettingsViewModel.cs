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

    public bool IsCustomPresetSelected => SelectedPreset.Id == DnsPresetCatalog.Custom.Id;

    public DnsSettingsViewModel(
        INetworkAdapterService adapterService,
        IDnsConfigurationService dnsService,
        IDnsCacheService cacheService,
        ISettingsService settingsService,
        IGhostAdapterService? ghostAdapterService = null)
    {
        _adapterService = adapterService;
        _dnsService = dnsService;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _ghostAdapterService = ghostAdapterService;

        _ = LoadAdaptersAsync();
        if (_ghostAdapterService is not null)
        {
            _ = LoadGhostAdaptersAsync();
        }
    }

    partial void OnSelectedPresetChanged(DnsProviderPreset value) => OnPropertyChanged(nameof(IsCustomPresetSelected));

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
            var results = await _dnsService.ApplyAsync(SelectedAdapter.Id, servers);
            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            _settingsService.Current.LastSelectedAdapterId = SelectedAdapter.Id;
            await _settingsService.SaveAsync();

            StatusText = results.All(r => r.Success)
                ? $"{SelectedAdapter.DisplayName} に DNS を適用しました"
                : "一部のコマンドが失敗しました。ログを確認してください";

            await LoadAdaptersAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

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
