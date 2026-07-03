using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Services;
using Shisui.UI.Services;

namespace Shisui.UI.ViewModels;

/// <summary>
/// バージョン情報・自動更新タブ。
/// </summary>
public partial class VersionViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string versionText = "…";

    [ObservableProperty]
    private string updateStatusText = string.Empty;

    [ObservableProperty]
    private bool isChecking;

    [ObservableProperty]
    private bool isUpdateReady;

    [ObservableProperty]
    private bool checkForUpdatesOnStartup;

    public VersionViewModel(UpdateService updateService, ISettingsService settingsService)
    {
        _updateService = updateService;
        _settingsService = settingsService;
        VersionText = updateService.CurrentVersion;
        CheckForUpdatesOnStartup = settingsService.Current.CheckForUpdatesOnStartup;
    }

    public void Initialize()
    {
        if (_settingsService.Current.CheckForUpdatesOnStartup)
        {
            _ = CheckForUpdateAsync();
        }
    }

    partial void OnCheckForUpdatesOnStartupChanged(bool value)
    {
        _settingsService.Current.CheckForUpdatesOnStartup = value;
        _ = _settingsService.SaveAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CheckForUpdateAsync()
    {
        IsChecking = true;
        UpdateStatusText = "更新を確認しています…";
        try
        {
            var result = await _updateService.CheckAsync();
            UpdateStatusText = result.Message;
            IsUpdateReady = result.Outcome == UpdateService.UpdateOutcome.UpdateReady;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DownloadAndApplyAsync()
    {
        IsChecking = true;
        UpdateStatusText = "更新をダウンロードしています…（完了後に自動で再起動します）";
        try
        {
            await _updateService.DownloadAndApplyAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"更新の適用に失敗しました: {ex.Message}";
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(AppPaths.LogsDirectory);

    [RelayCommand]
    private void OpenSettingsFolder() => OpenFolder(AppPaths.AppDataDirectory);

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("フォルダを開けませんでした", ex);
        }
    }
}
