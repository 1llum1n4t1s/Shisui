using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Services;
using Shisui.UI.Services;
using VelopackUpdateDialog;

namespace Shisui.UI.ViewModels;

/// <summary>
/// バージョン情報・自動更新タブ。更新の確認 / ダウンロード / 適用は VelopackUpdateDialog.Avalonia の
/// <see cref="UpdateDialogWindow"/> に委譲する (自前の進捗 UI は持たない)。
/// </summary>
public partial class VersionViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private readonly ISettingsService _settingsService;
    private bool _updateDialogOpen;
    private bool _restoringSettings;

    [ObservableProperty]
    private bool isSavingSettings;

    [ObservableProperty]
    private string versionText = "…";

    [ObservableProperty]
    private string updateStatusText = string.Empty;

    [ObservableProperty]
    private bool isChecking;

    [ObservableProperty]
    private bool checkForUpdatesOnStartup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIgnoredUpdateTag))]
    private string ignoredUpdateTag = string.Empty;

    /// <summary>「このバージョンをスキップ」で無視中の更新があるか (再表示ボタンの表示制御)。</summary>
    public bool HasIgnoredUpdateTag => !string.IsNullOrEmpty(IgnoredUpdateTag);

    public VersionViewModel(UpdateService updateService, ISettingsService settingsService)
    {
        _updateService = updateService;
        _settingsService = settingsService;
        VersionText = updateService.CurrentVersion;
        checkForUpdatesOnStartup = settingsService.Current.CheckForUpdatesOnStartup;
        IgnoredUpdateTag = settingsService.Current.IgnoreUpdateTag ?? string.Empty;
    }

    public void Initialize()
    {
        if (_settingsService.Current.CheckForUpdatesOnStartup)
        {
            // ここは MainWindowViewModel ctor 経由で desktop.MainWindow 代入より前に呼ばれるため、
            // owner ウィンドウが確定してから走るよう Background 優先度で遅延投入する。
            Dispatcher.UIThread.Post(
                () => _ = ShowUpdateDialogAsync(manually: false),
                DispatcherPriority.Background);
        }
    }

    partial void OnCheckForUpdatesOnStartupChanged(bool value)
    {
        if (!_restoringSettings)
        {
            _ = SaveCheckForUpdatesOnStartupAsync(value);
        }
    }

    private async Task SaveCheckForUpdatesOnStartupAsync(bool value)
    {
        var previous = _settingsService.Current.CheckForUpdatesOnStartup;
        if (IsSavingSettings)
        {
            RestoreCheckForUpdatesOnStartup(previous);
            return;
        }

        IsSavingSettings = true;
        _settingsService.Current.CheckForUpdatesOnStartup = value;
        try
        {
            await _settingsService.SaveAsync();
            UpdateStatusText = "起動時の更新確認設定を保存しました";
        }
        catch (Exception ex)
        {
            _settingsService.Current.CheckForUpdatesOnStartup = previous;
            RestoreCheckForUpdatesOnStartup(previous);
            ReportSettingsSaveFailure(ex);
        }
        finally
        {
            IsSavingSettings = false;
        }
    }

    private void RestoreCheckForUpdatesOnStartup(bool value)
    {
        _restoringSettings = true;
        try
        {
            CheckForUpdatesOnStartup = value;
        }
        finally
        {
            _restoringSettings = false;
        }
    }

    [RelayCommand]
    private Task CheckForUpdateAsync() => ShowUpdateDialogAsync(manually: true);

    [RelayCommand]
    private async Task ClearIgnoredUpdateTagAsync()
    {
        if (string.IsNullOrEmpty(IgnoredUpdateTag))
        {
            return;
        }

        await SaveIgnoredUpdateTagAsync(null);
    }

    private async Task SaveIgnoredUpdateTagAsync(string? tag)
    {
        if (IsSavingSettings)
        {
            UpdateStatusText = "設定を保存中です。完了後にもう一度操作してください";
            return;
        }

        var previous = _settingsService.Current.IgnoreUpdateTag;
        IsSavingSettings = true;
        _settingsService.Current.IgnoreUpdateTag = tag;
        try
        {
            await _settingsService.SaveAsync();
            IgnoredUpdateTag = tag ?? string.Empty;
            UpdateStatusText = "更新のスキップ設定を保存しました";
        }
        catch (Exception ex)
        {
            _settingsService.Current.IgnoreUpdateTag = previous;
            ReportSettingsSaveFailure(ex);
        }
        finally
        {
            IsSavingSettings = false;
        }
    }

    private void ReportSettingsSaveFailure(Exception ex)
    {
        UpdateStatusText = "アプリの設定を保存できませんでした。変更を戻しました。ログを確認してください";
        LoggerBootstrap.Log.Error("更新設定を保存できませんでした", ex);
    }

    /// <summary>
    /// VelopackUpdateDialog を表示する。<paramref name="manually"/>=true は手動チェック
    /// (最新でも結果を表示・スキップタグを無視)、false は起動時自動チェック
    /// (更新があるときだけ・スキップタグ一致は表示しない)。
    /// </summary>
    private async Task ShowUpdateDialogAsync(bool manually)
    {
        if (_updateDialogOpen)
        {
            return;
        }

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            // MainWindow 未確定 (通常起こらない)。owner なしでは表示しない。
            return;
        }

        var manager = _updateService.TryCreateInstalledManager();
        if (manager is null)
        {
            if (manually)
            {
                UpdateStatusText = "開発ビルドでは更新を確認できません。インストール版をご利用ください";
            }

            return;
        }

        _updateDialogOpen = true;
        IsChecking = true;
        UpdateStatusText = string.Empty;
        try
        {
            var options = new UpdateDialogOptions
            {
                Strings = ShisuiUpdateStrings.Instance,
                IgnoredTagName = _settingsService.Current.IgnoreUpdateTag,
                AccentBrush = new SolidColorBrush(Color.Parse("#0A84FF")),
            };
            options.VersionIgnored += tag => Dispatcher.UIThread.Post(
                () => _ = SaveIgnoredUpdateTagAsync(tag));
            options.ErrorOccurred += ex =>
                LoggerBootstrap.Log.Error($"Velopack 更新失敗: {ex.GetType().Name}", ex);

            await UpdateDialogWindow.ShowAsync(owner, manager, options, manualCheck: manually);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"更新の確認に失敗しました: {ex.Message}";
            LoggerBootstrap.Log.Error("更新ダイアログの表示に失敗しました", ex);
        }
        finally
        {
            (manager as IDisposable)?.Dispose();
            IsChecking = false;
            _updateDialogOpen = false;
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
