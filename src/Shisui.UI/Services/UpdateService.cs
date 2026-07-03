using System;
using System.Threading;
using System.Threading.Tasks;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Velopack;
using Velopack.Sources;

namespace Shisui.UI.Services;

/// <summary>
/// Velopack の SimpleWebSource ベースで Cloudflare R2 (shisui.nephilim.jp) から更新を取得する。
/// 配信元 URL は <see cref="AppSettings.UpdateBaseUrl"/> にハードコード固定 (settings.json から書き換え不可)。
/// </summary>
public sealed class UpdateService
{
    private const int CheckTimeoutMs = 15000;

    public enum UpdateOutcome
    {
        UpToDate,
        UpdateReady,
        NotInstalled,
        Error,
    }

    public sealed record CheckResult(UpdateOutcome Outcome, string? NewVersion, string Message);

    private readonly AppSettings _settings;
    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    public UpdateService(AppSettings settings) => _settings = settings;

    /// <summary>実行中アプリのバージョン。開発実行時は "開発ビルド"。</summary>
    public string CurrentVersion
    {
        get
        {
            try
            {
                var mgr = BuildManager();
                return mgr.IsInstalled && mgr.CurrentVersion is not null ? mgr.CurrentVersion.ToString() : "開発ビルド";
            }
            catch
            {
                return "開発ビルド";
            }
        }
    }

    private UpdateManager BuildManager()
    {
        // SimpleWebSource は {baseUrl}/releases.{channel}.json を取得する。
        var source = new SimpleWebSource(_settings.UpdateBaseUrl);
        var options = new UpdateOptions { ExplicitChannel = _settings.UpdateChannel };
        return _manager ??= new UpdateManager(source, options);
    }

    public async Task<CheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var manager = BuildManager();
            if (!manager.IsInstalled)
            {
                // dotnet run 等の開発実行では更新機構が働かない (インストール版でのみ有効)。
                return new CheckResult(UpdateOutcome.NotInstalled, null, "開発ビルドのため更新確認はスキップされます (インストール版でのみ動作)");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CheckTimeoutMs);

            var updateInfo = await manager.CheckForUpdatesAsync().WaitAsync(cts.Token);
            if (updateInfo is null)
            {
                return new CheckResult(UpdateOutcome.UpToDate, null, "お使いのバージョンは最新です");
            }

            _pendingUpdate = updateInfo;
            var version = updateInfo.TargetFullRelease.Version.ToString();
            return new CheckResult(UpdateOutcome.UpdateReady, version, $"新しいバージョン {version} が利用可能です");
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("更新確認に失敗しました", ex);
            return new CheckResult(UpdateOutcome.Error, null, $"更新確認に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 検出済みの更新をダウンロードして適用し、アプリを再起動する。呼び出し後はプロセスが終了する。
    /// </summary>
    public async Task DownloadAndApplyAsync(CancellationToken ct = default)
    {
        if (_manager is null || _pendingUpdate is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pendingUpdate, cancelToken: ct);
        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
