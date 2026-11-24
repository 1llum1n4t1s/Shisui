using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.UI.ViewModels;

/// <summary>結果表の 1 行 (無負荷 / 負荷・上限なし / 負荷・上限あり)。</summary>
public sealed record UplinkResultRow(string Label, string RttP90, string Loss, string Throughput);

/// <summary>
/// 上り速度の制限タブ (Windows 専用)。回線の計測と QoS ポリシーによる上限の適用・解除・起動時照合を行う。
/// 計測・適用・解除・照合は変更ゲートを 1 回だけ取得して行い (サービスは再取得しない)、
/// 適用はこの画面の明示操作でだけ行う。おまかせ (自動最適化) からは呼ばれない。
/// </summary>
public partial class UplinkLimitViewModel : ObservableObject, IDisposable
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    public const string PolicyScopeNotice =
        "この PC から送信する通信すべてが対象です。家庭内 LAN への転送（NAS へのコピーなど）も同じ上限まで遅くなります。";

    public const string RebootNotice =
        "PC を再起動すると解除されます。Shisui を起動すると同じ回線でだけ再適用します。Shisui の起動中は、同じ回線に戻ると再適用します。";

    public const string MonitoringNotice =
        "Shisui が起動していない間の回線変更は検知できません。回線を変えたら Shisui を起動して確認してください。";

    public const string MeasurementEstimate =
        "計測の通信量の目安は、上り 10 Mbps で約 0.15 GB、35 Mbps で約 0.5 GB です。通信量 2 GB または 5 分に達すると打ち切ります。"
        + "上り約 120 Mbps を超える回線では上限に届き、完了しないことがあります。従量制の回線では実行しないでください。";

    public const string ForcedTerminationNotice =
        "計測中に PC の電源断や強制終了があると、次に Shisui を起動するか PC を再起動するまで試験用の上限が残ることがあります。";

    /// <summary>アプリ終了時に計測の後始末の完了を待つ上限時間。</summary>
    public static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(15);

    private const int MaxDeferrals = 2;

    private readonly IUplinkLimitService _limitService;
    private readonly IUplinkMeasurementService _measurementService;
    private readonly ISettingsService _settingsService;
    private readonly INetworkMutationGate _gate;
    private readonly TimeProvider _timeProvider;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    private CancellationTokenSource? _measurementCts;
    private CancellationTokenSource? _debounceCts;
    private UplinkMeasurementReport? _proposalReport;
    private UplinkLineIdentity _currentLine = UplinkLineIdentity.Unknown;
    private Task? _measureTask;
    private long? _previousLimit;
    private bool _pendingReconcile;
    private bool _reconcileActive;
    private bool _monitoring;
    private bool _disposed;

    public UplinkLimitViewModel(
        IUplinkLimitService limitService,
        IUplinkMeasurementService measurementService,
        ISettingsService settingsService,
        INetworkMutationGate gate,
        TimeProvider? timeProvider = null)
    {
        _limitService = limitService;
        _measurementService = measurementService;
        _settingsService = settingsService;
        _gate = gate;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>実行ログへ流すコマンド結果・通知 (合成結果を含む)。</summary>
    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    /// <summary>回線変更の通知をまとめて待つ時間。テストで短縮する。</summary>
    public TimeSpan NetworkChangeDebounce { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>回線を特定できないときの再試行回数と間隔 (起動直後・スリープ復帰・切替中の一時的な欠落を待つ)。</summary>
    public int LineRetryCount { get; set; } = 5;

    public TimeSpan LineRetryInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>待機。テストで実時間を待たないよう差し替える。</summary>
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } = (delay, ct) => Task.Delay(delay, ct);

    public ObservableCollection<UplinkResultRow> ResultRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveLimitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyProposalCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestorePreviousLimitCommand))]
    private bool isBusy;

    /// <summary>計測中 (中止ボタンの表示条件)。</summary>
    [ObservableProperty]
    private bool isMeasuring;

    /// <summary>子プロセスの終了を確認できず、PC 再起動までは変更操作を止めている。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveLimitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyProposalCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestorePreviousLimitCommand))]
    private bool isOperationBlocked;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string lineText = "回線: 確認中…";

    [ObservableProperty]
    private string limitStateText = "上限: 確認中…";

    [ObservableProperty]
    private string savedMeasurementText = "保存済みの計測: 確認中…";

    [ObservableProperty]
    private bool isMeasurementStale;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private bool costAcknowledged;

    [ObservableProperty]
    private bool hasResult;

    [ObservableProperty]
    private string verdictText = string.Empty;

    [ObservableProperty]
    private string proposalText = string.Empty;

    [ObservableProperty]
    private string cleanupWarningText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyProposalCommand))]
    private bool hasProposal;

    /// <summary>計測のために外した適用済みの上限があり、元の値へ戻せる。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestorePreviousLimitCommand))]
    private bool hasPreviousLimit;

    [ObservableProperty]
    private string previousLimitButtonText = "計測前の上限へ戻す";

    public bool HasCleanupWarning => CleanupWarningText.Length > 0;

    partial void OnCleanupWarningTextChanged(string value) => OnPropertyChanged(nameof(HasCleanupWarning));

    private bool CanOperate() => !IsBusy && !IsOperationBlocked;

    private bool CanRefresh() => !IsBusy;

    private bool CanStartMeasurement() => CanOperate() && CostAcknowledged;

    private bool CanApplyProposal() => CanOperate() && HasProposal;

    private bool CanRestorePreviousLimit() => CanOperate() && HasPreviousLimit;

    // ───────────── 起動・回線変更の照合 ─────────────

    /// <summary>起動時の照合を始め、回線変更の購読を開始する (UI スレッドで 1 回だけ呼ぶ)。</summary>
    public void Start()
    {
        if (_disposed || _monitoring)
        {
            return;
        }

        _monitoring = true;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        _ = InitializeAsync();
    }

    /// <summary>
    /// 起動時の照合。試験用の残骸は削除し、適用用は「保存済み・有効・回線一致」なら維持・再適用・値の補正、
    /// それ以外は削除して通知する。設定の保存は行わない。
    /// 先にゲートなしで状態だけを読み、変更が必要なときだけゲート付き (処理中表示あり) で読み直して処理する。
    /// </summary>
    public Task InitializeAsync() => ReconcileFlowAsync(startup: true);

    /// <summary>
    /// 回線変更後の照合。不一致なら適用用を解除し、保存済み・有効・回線一致なら (解除後に元の回線へ戻った場合を含め) 再適用・補正する。
    /// 有効な保存がなければ何もしない (PowerShell も起動しない)。
    /// </summary>
    public Task ReconcileAfterNetworkChangeAsync() => ReconcileFlowAsync(startup: false);

    /// <summary>回線変更の通知を擬似的に発生させる (テスト用)。</summary>
    public void NotifyNetworkChanged() => OnNetworkAddressChanged(this, EventArgs.Empty);

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_uiContext is null)
        {
            BeginDebounce();
            return;
        }

        _uiContext.Post(_ => BeginDebounce(), null);
    }

    private void BeginDebounce()
    {
        if (_disposed)
        {
            return;
        }

        var next = new CancellationTokenSource();
        Interlocked.Exchange(ref _debounceCts, next)?.Cancel();
        _ = DebounceAsync(next.Token);
    }

    private async Task DebounceAsync(CancellationToken ct)
    {
        try
        {
            await DelayAsync(NetworkChangeDebounce, ct);
            if (IsBusy)
            {
                // 計測中の回線変化は計測サービス側が扱う。他の処理中は終了後にまとめて照合する。
                _pendingReconcile = true;
                return;
            }

            await ReconcileAfterNetworkChangeAsync();
        }
        catch (OperationCanceledException)
        {
            // 新しい変更通知に置き換えられた。
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("回線変更の照合で例外が発生しました", ex);
        }
    }

    /// <summary>照合で OS に行う変更。すべて false/null なら変更なし (ゲート・処理中表示は不要)。</summary>
    private sealed record ReconcilePlan(bool RemoveTrial, bool RemoveLimit, long? ApplyLimit, string? Notice)
    {
        public bool IsEmpty => !RemoveTrial && !RemoveLimit && ApplyLimit is null;
    }

    private bool HasEnabledProfile() => _settingsService.Current.UplinkLimitProfiles.Any(profile => profile.IsEnabled);

    private async Task ReconcileFlowAsync(bool startup)
    {
        if (_disposed || (!startup && !HasEnabledProfile()))
        {
            return;
        }

        if (_reconcileActive)
        {
            _pendingReconcile = true;
            return;
        }

        _reconcileActive = true;
        try
        {
            var deferrals = 0;
            do
            {
                _pendingReconcile = false;
                if (IsBusy)
                {
                    // 他の処理の終了時 (RunOperationAsync) にまとめて照合する。
                    _pendingReconcile = true;
                    return;
                }

                if (!startup && !HasEnabledProfile())
                {
                    return;
                }

                var deferred = await ReconcileOnceAsync(startup, deferrals < MaxDeferrals);
                if (deferred)
                {
                    deferrals++;
                    _pendingReconcile = true;
                }
                else
                {
                    startup = false;
                }
            }
            while (_pendingReconcile && !_disposed);
        }
        finally
        {
            _reconcileActive = false;
        }
    }

    /// <summary>1 回の照合。ゲート内で回線が一時的に消えたため判断を見送ったときだけ true。</summary>
    private async Task<bool> ReconcileOnceAsync(bool startup, bool canDefer)
    {
        var state = await ReadStateStableAsync();
        ShowState(state);
        if (!state.IsReadable)
        {
            if (startup && HasEnabledProfile())
            {
                Notify(false, "上り速度制限の照合", "QoS ポリシーを読み取れないため、上り速度制限の照合をできませんでした。管理者権限で起動しているか確認してください。");
            }

            return false;
        }

        var plan = PlanReconcile(state, startup);
        if (plan.IsEmpty)
        {
            if (plan.Notice is not null)
            {
                Notify(false, "上り速度制限の照合", plan.Notice);
            }

            return false;
        }

        if (IsBusy)
        {
            _pendingReconcile = true;
            return false;
        }

        var deferred = false;
        var deferIfLineLost = canDefer && state.Line.IsIdentified;
        await RunOperationAsync(
            startup ? "上り速度制限の状態を確認しています…" : "回線の変更を確認しています…",
            async _ => deferred = await ReconcileUnderGateAsync(startup, deferIfLineLost));
        return deferred;
    }

    /// <summary>
    /// ゲートなしで状態を読む。回線を特定できず、判断に回線が要る (適用中の上限または有効な保存がある) ときは、
    /// 一時的な欠落を待って数回読み直す。待機中はゲートを保持しない。
    /// </summary>
    private async Task<UplinkLimitState> ReadStateStableAsync()
    {
        var state = await _limitService.GetStateAsync();
        for (var attempt = 0; attempt < LineRetryCount && NeedsLineRetry(state) && !_disposed; attempt++)
        {
            await DelayAsync(LineRetryInterval, CancellationToken.None);
            state = await _limitService.GetStateAsync();
        }

        return state;
    }

    private bool NeedsLineRetry(UplinkLimitState state) =>
        state.IsReadable && !state.Line.IsIdentified && (state.LimitPolicy is not null || HasEnabledProfile());

    private ReconcilePlan PlanReconcile(UplinkLimitState state, bool startup)
    {
        var enabled = FindEnabledProfile(state.Line);
        var trialRemnant = state.TrialPolicy is not null;
        if (enabled?.LimitPolicyBitsPerSecond is { } saved && UplinkLimitPolicy.IsValidRate(saved))
        {
            if (state.LimitPolicy is { IsDefaultTemplate: true } current && current.ThrottleBitsPerSecond == saved)
            {
                return new ReconcilePlan(startup && trialRemnant, false, null, null);
            }

            return state.OtherPolicyNames.Count > 0
                ? new ReconcilePlan(startup && trialRemnant, false, null, $"他の QoS ポリシーがあるため、保存済みの上限を適用できません: {string.Join("、", state.OtherPolicyNames)}")
                : new ReconcilePlan(trialRemnant, false, saved, null);
        }

        // 回線不一致・有効な保存なし: 他の QoS ポリシーがあっても自名のポリシーの削除は続行する (適用だけを拒否する)。
        return new ReconcilePlan(startup && trialRemnant, state.LimitPolicy is not null, null, null);
    }

    private async Task<bool> ReconcileUnderGateAsync(bool startup, bool deferIfLineLost)
    {
        // 待機中に状態が変わりうるため、ゲート内で読み直してから判断する。
        var state = await _limitService.GetStateAsync();
        ShowState(state);
        if (!state.IsReadable)
        {
            return false;
        }

        if (deferIfLineLost && !state.Line.IsIdentified)
        {
            return true;
        }

        var plan = PlanReconcile(state, startup);
        if (plan.RemoveTrial && !await RemoveTrialRemnantAsync())
        {
            return false;
        }

        if (plan.RemoveLimit && !await RemoveMismatchedAsync(startup, state))
        {
            return false;
        }

        if (plan.ApplyLimit is { } target && !await ReapplyAsync(target, corrected: state.LimitPolicy is not null))
        {
            return false;
        }

        if (plan.Notice is not null)
        {
            Notify(false, "上り速度制限の照合", plan.Notice);
        }

        return false;
    }

    /// <summary>試験用の残骸を削除する。子プロセスの終了を確認できず操作を止めたときは false。</summary>
    private async Task<bool> RemoveTrialRemnantAsync()
    {
        var trial = await _limitService.RemoveTrialAsync();
        Forward(trial);
        if (!trial.Success)
        {
            Notify(false, "上り速度制限 試験用の削除", $"試験用ポリシーの残骸を削除できませんでした: {trial.Message}");
        }

        if (!trial.RequiresOperationAbort)
        {
            ShowState(await _limitService.GetStateAsync());
            return true;
        }

        BlockOperations();
        return false;
    }

    private async Task<bool> ReapplyAsync(long bitsPerSecond, bool corrected)
    {
        var result = await _limitService.ApplyLimitAsync(bitsPerSecond);
        Forward(result);
        var done = corrected ? "保存済みの値へ補正しました" : "再適用しました";
        Notify(
            result.Success,
            corrected ? "上り速度制限の補正" : "上り速度制限の再適用",
            result.Success
                ? $"同じ回線のため、上り速度制限 {FormatMbps(bitsPerSecond)} を{done}。"
                : $"上り速度制限を{(corrected ? "補正" : "再適用")}できませんでした: {result.Message}");
        if (result.RequiresOperationAbort)
        {
            BlockOperations();
            return false;
        }

        ShowState(await _limitService.GetStateAsync());
        return true;
    }

    private async Task<bool> RemoveMismatchedAsync(bool startup, UplinkLimitState state)
    {
        var reason = !state.Line.IsIdentified
            ? "回線を確認できない"
            : FindProfile(state.Line) is null
                ? "この回線の保存済み設定がない（設定を読み込めなかった場合を含む）"
                : "この回線では上限が有効になっていない";
        var result = await _limitService.RemoveLimitAsync();
        Forward(result);
        var when = startup ? "起動時に" : "回線の変更を検知し、";
        Notify(
            result.Success,
            "上り速度制限の解除",
            result.Success
                ? $"{when}{reason}ため、適用中の上り速度制限を解除しました。"
                : $"{when}{reason}ため解除しようとしましたが失敗しました: {result.Message}");
        if (result.RequiresOperationAbort)
        {
            BlockOperations();
            return false;
        }

        ShowState(await _limitService.GetStateAsync());
        return true;
    }

    // ───────────── 現在の状態 ─────────────

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RunOperationAsync("現在の状態を読み取っています…", async _ =>
    {
        ShowState(await _limitService.GetStateAsync());
        StatusText = "現在の状態を更新しました。";
    }, useGate: false);

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task RemoveLimitAsync() => RunOperationAsync("上り速度制限を解除しています…", async _ =>
    {
        var result = await _limitService.RemoveLimitAsync();
        Forward(result);
        if (result.RequiresOperationAbort)
        {
            BlockOperations();
            return;
        }

        if (!result.Success)
        {
            StatusText = $"上り速度制限を解除できませんでした: {result.Message}";
            ShowState(await _limitService.GetStateAsync());
            return;
        }

        var state = await _limitService.GetStateAsync();
        ShowState(state);
        if (await TryDisableProfilesAsync(state.Line))
        {
            StatusText = "上り速度制限を解除しました。";
        }
        else
        {
            StatusText = "上り速度制限は解除しましたが、設定を保存できませんでした。次回の Shisui 起動時や回線の変更時に再適用される可能性があります。";
        }
    });

    private async Task<bool> TryDisableProfilesAsync(UplinkLineIdentity line)
    {
        var targets = _settingsService.Current.UplinkLimitProfiles
            .Where(profile => profile.IsEnabled && (!line.IsIdentified || line.Matches(profile.AdapterGuid, profile.GatewayMac)))
            .ToList();
        if (targets.Count == 0)
        {
            return true;
        }

        foreach (var profile in targets)
        {
            profile.IsEnabled = false;
        }

        if (await TrySaveAsync())
        {
            return true;
        }

        foreach (var profile in targets)
        {
            profile.IsEnabled = true;
        }

        return false;
    }

    // ───────────── 計測 ─────────────

    [RelayCommand(CanExecute = nameof(CanStartMeasurement))]
    private async Task StartMeasurementAsync()
    {
        using var cts = new CancellationTokenSource();
        _measurementCts = cts;
        try
        {
            await RunOperationAsync("計測を準備しています…", ct => MeasureCoreAsync(ct), ct: cts.Token);
        }
        finally
        {
            _measurementCts = null;
            CostAcknowledged = false;
        }
    }

    [RelayCommand]
    private void CancelMeasurement()
    {
        try
        {
            _measurementCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 計測はすでに終わっている。
        }
    }

    /// <summary>
    /// アプリ終了時。回線変更の購読を外し、計測中なら中止して、計測サービスの後始末 (試験用ポリシーの削除) の完了を
    /// <paramref name="timeout"/> まで待つ。計測は Task.Run で UI の同期コンテキスト外から始めているため、
    /// UI スレッドで同期的に待っても継続が UI スレッドを要求せずデッドロックしない。何度呼んでもよい。
    /// </summary>
    public void Shutdown(TimeSpan timeout)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_monitoring)
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            }

            Interlocked.Exchange(ref _debounceCts, null)?.Cancel();
        }

        CancelMeasurement();
        var task = _measureTask;
        if (task is null || task.IsCompleted)
        {
            return;
        }

        try
        {
            task.Wait(timeout);
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("終了時の計測の後始末で例外が発生しました", ex);
        }
    }

    private async Task MeasureCoreAsync(CancellationToken ct)
    {
        IsMeasuring = true;
        ClearResult();
        try
        {
            // 計測が適用済みの上限を外すため、先に元の値を控える (結果が提案のとき、戻す手段を示す)。
            var before = await _limitService.GetStateAsync(ct);
            long? previous = before is { IsReadable: true, LimitPolicy: { IsDefaultTemplate: true } limit }
                             && UplinkLimitPolicy.IsValidRate(limit.ThrottleBitsPerSecond)
                ? limit.ThrottleBitsPerSecond
                : null;
            var progress = new Progress<UplinkMeasurementProgress>(p => ProgressText = FormatProgress(p));
            var measure = Task.Run(() => _measurementService.MeasureAsync(progress, ct), CancellationToken.None);
            _measureTask = measure;
            var report = await measure;
            foreach (var command in report.Commands)
            {
                CommandExecuted?.Invoke(this, command);
            }

            ShowReport(report, previous, ct.IsCancellationRequested);
            if (report.RequiresOperationAbort)
            {
                BlockOperations();
                return;
            }

            if (_disposed)
            {
                return;
            }

            // 中止・失敗・未完了の計測で、完了済みの計測を上書きしない。
            if (report.Outcome is UplinkMeasurementOutcome.Recommended or UplinkMeasurementOutcome.NoChangeNeeded
                    or UplinkMeasurementOutcome.NoImprovement
                && report.Line.IsIdentified && report.CapacityBitsPerSecond is not null)
            {
                var saved = await TrySaveMeasurementAsync(report);
                if (!saved)
                {
                    StatusText += " 計測結果を設定へ保存できませんでした。";
                }
            }

            ShowState(await _limitService.GetStateAsync());
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private async Task<bool> TrySaveMeasurementAsync(UplinkMeasurementReport report)
    {
        var profiles = _settingsService.Current.UplinkLimitProfiles;
        var existing = FindProfile(report.Line);
        var snapshot = existing is null ? null : CloneProfile(existing);
        var profile = existing ?? NewProfile(report.Line);
        FillMeasurement(profile, report);
        if (report.Outcome == UplinkMeasurementOutcome.Recommended)
        {
            profile.LimitPolicyBitsPerSecond = report.RecommendedPolicyBitsPerSecond;
            profile.IsEnabled = false; // 適用は利用者の明示操作まで行わない。
        }

        if (existing is null)
        {
            profiles.Add(profile);
        }

        if (await TrySaveAsync())
        {
            return true;
        }

        // 保存できなかった変更はメモリ上も戻し、ディスクと食い違わせない。
        if (existing is null)
        {
            profiles.Remove(profile);
        }
        else
        {
            CopyProfile(snapshot!, existing);
        }

        return false;
    }

    // ───────────── 適用 ─────────────

    [RelayCommand(CanExecute = nameof(CanApplyProposal))]
    private Task ApplyProposalAsync() =>
        RunOperationAsync("上り速度制限を適用しています…", _ => ApplyReportLimitAsync(useProposal: true));

    /// <summary>計測のために外した計測前の上限を、保存 → 適用の同じ手順で元の値へ戻す。</summary>
    [RelayCommand(CanExecute = nameof(CanRestorePreviousLimit))]
    private Task RestorePreviousLimitAsync() =>
        RunOperationAsync("計測前の上限へ戻しています…", _ => ApplyReportLimitAsync(useProposal: false));

    private async Task ApplyReportLimitAsync(bool useProposal)
    {
        var report = _proposalReport;
        var chosen = useProposal ? report?.RecommendedPolicyBitsPerSecond : _previousLimit;
        if (report is null || chosen is not { } target || report.CapacityBitsPerSecond is null)
        {
            StatusText = "適用できる提案がありません。先に計測してください。";
            return;
        }

        var state = await _limitService.GetStateAsync();
        ShowState(state);
        var rejection = CheckApplicable(state, report);
        if (rejection is not null)
        {
            StatusText = rejection;
            return;
        }

        // 順序: 設定保存 (IsEnabled=true) → 適用 → 読み戻し。保存に失敗したら OS は変更しない。
        var existing = FindProfile(report.Line);
        var snapshot = existing is null ? null : CloneProfile(existing);
        var profile = existing ?? NewProfile(report.Line);
        if (useProposal || existing is null)
        {
            FillMeasurement(profile, report);
        }

        profile.LimitPolicyBitsPerSecond = target;
        profile.IsEnabled = true;
        if (existing is null)
        {
            _settingsService.Current.UplinkLimitProfiles.Add(profile);
        }

        if (!await TrySaveAsync())
        {
            if (existing is null)
            {
                _settingsService.Current.UplinkLimitProfiles.Remove(profile);
            }
            else
            {
                CopyProfile(snapshot!, existing);
            }

            StatusText = "設定を保存できなかったため、上り速度制限は適用していません。";
            return;
        }

        var result = await _limitService.ApplyLimitAsync(target);
        Forward(result);
        if (result.RequiresOperationAbort)
        {
            BlockOperations();
        }

        ShowState(await _limitService.GetStateAsync());
        if (result.Success)
        {
            StatusText = useProposal
                ? $"上り速度制限 {FormatMbps(target)} を適用しました。"
                : $"計測前の上り速度制限 {FormatMbps(target)} へ戻しました。";
            HasProposal = false;
            HasPreviousLimit = false;
            _previousLimit = null;
            return;
        }

        profile.IsEnabled = false;
        var disabledSaved = await TrySaveAsync();
        StatusText = $"上り速度制限を適用できませんでした: {result.Message}"
                     + (disabledSaved ? " 次回起動時の再適用は無効にしました。" : " 再適用の設定を無効にできなかったため、次回起動時や回線の変更時に再試行される可能性があります。");
    }

    private static string? CheckApplicable(UplinkLimitState state, UplinkMeasurementReport report)
    {
        if (!state.IsReadable)
        {
            return "QoS ポリシーを読み取れないため適用しません。";
        }

        if (state.OtherPolicyNames.Count > 0)
        {
            return $"他の QoS ポリシーがあるため適用しません: {string.Join("、", state.OtherPolicyNames)}";
        }

        if (state.Line.Kind == UplinkAdapterKind.Virtual)
        {
            return "既定経路が仮想アダプターのため適用しません。";
        }

        if (!state.Line.Matches(report.Line.AdapterGuid, report.Line.GatewayMac))
        {
            return "計測した回線と現在の回線が異なるため適用しません。もう一度計測してください。";
        }

        return null;
    }

    // ───────────── 共通 ─────────────

    private async Task RunOperationAsync(string text, Func<CancellationToken, Task> body, bool useGate = true, CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ProgressText = text;
        try
        {
            using var lease = useGate ? await _gate.EnterAsync(ct) : null;
            await body(ct);
        }
        catch (OperationCanceledException)
        {
            StatusText = ct.IsCancellationRequested ? "計測を中止しました。" : "処理を中止しました。";
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("上り速度制限の処理で例外が発生しました", ex);
            StatusText = $"処理中にエラーが発生しました: {ex.Message}";
            Notify(false, "上り速度制限", StatusText);
        }
        finally
        {
            IsMeasuring = false;
            ProgressText = string.Empty;
            IsBusy = false;
        }

        if (_pendingReconcile && !_disposed)
        {
            _pendingReconcile = false;
            await ReconcileAfterNetworkChangeAsync();
        }
    }

    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await _settingsService.SaveAsync();
            return true;
        }
        catch (Exception ex)
        {
            LoggerBootstrap.Log.Error("上り速度制限の設定を保存できませんでした", ex);
            Notify(false, "上り速度制限の設定保存", $"設定を保存できませんでした: {ex.Message}");
            return false;
        }
    }

    private void BlockOperations()
    {
        IsOperationBlocked = true;
        StatusText = "子プロセスの終了を確認できませんでした。PC を再起動してから操作してください。";
    }

    private void Forward(UplinkLimitOperationResult result)
    {
        foreach (var command in result.Commands)
        {
            CommandExecuted?.Invoke(this, command);
        }
    }

    private void Notify(bool success, string title, string message)
    {
        if (!success)
        {
            StatusText = message;
        }

        CommandExecuted?.Invoke(this, new CommandExecutionResult(success, title, success ? 0 : 1, message, success ? string.Empty : message));
    }

    private UplinkLimitProfile? FindProfile(UplinkLineIdentity line) =>
        line.IsIdentified
            ? _settingsService.Current.UplinkLimitProfiles.FirstOrDefault(p => line.Matches(p.AdapterGuid, p.GatewayMac))
            : null;

    private UplinkLimitProfile? FindEnabledProfile(UplinkLineIdentity line) =>
        FindProfile(line) is { IsEnabled: true } profile ? profile : null;

    private static UplinkLimitProfile NewProfile(UplinkLineIdentity line) =>
        new() { AdapterGuid = line.AdapterGuid, GatewayMac = line.GatewayMac };

    private static void FillMeasurement(UplinkLimitProfile profile, UplinkMeasurementReport report)
    {
        profile.MeasuredAtUtc = report.MeasuredAtUtc;
        profile.CapacityBitsPerSecond = report.CapacityBitsPerSecond ?? profile.CapacityBitsPerSecond;
        profile.Outcome = report.Outcome.ToString();
        profile.IdleRttP90Ms = report.Idle?.RttP90Ms;
        profile.LoadedRttP90Ms = report.Loaded?.RttP90Ms;
        profile.LimitedRttP90Ms = report.Limited?.RttP90Ms;
        profile.IdleLossPercent = report.Idle?.LossPercent;
        profile.LoadedLossPercent = report.Loaded?.LossPercent;
        profile.LimitedLossPercent = report.Limited?.LossPercent;
    }

    private static UplinkLimitProfile CloneProfile(UplinkLimitProfile source)
    {
        var copy = new UplinkLimitProfile();
        CopyProfile(source, copy);
        return copy;
    }

    private static void CopyProfile(UplinkLimitProfile from, UplinkLimitProfile to)
    {
        to.AdapterGuid = from.AdapterGuid;
        to.GatewayMac = from.GatewayMac;
        to.MeasuredAtUtc = from.MeasuredAtUtc;
        to.CapacityBitsPerSecond = from.CapacityBitsPerSecond;
        to.LimitPolicyBitsPerSecond = from.LimitPolicyBitsPerSecond;
        to.Outcome = from.Outcome;
        to.IsEnabled = from.IsEnabled;
        to.IdleRttP90Ms = from.IdleRttP90Ms;
        to.LoadedRttP90Ms = from.LoadedRttP90Ms;
        to.LimitedRttP90Ms = from.LimitedRttP90Ms;
        to.IdleLossPercent = from.IdleLossPercent;
        to.LoadedLossPercent = from.LoadedLossPercent;
        to.LimitedLossPercent = from.LimitedLossPercent;
    }

    // ───────────── 表示 ─────────────

    private void ShowState(UplinkLimitState state)
    {
        _currentLine = state.Line;
        LineText = FormatLine(state);
        LimitStateText = FormatLimit(state);
        ShowSavedMeasurement(state.Line);
    }

    private void ShowSavedMeasurement(UplinkLineIdentity line)
    {
        var profile = FindProfile(line);
        if (profile is null || profile.MeasuredAtUtc == default)
        {
            SavedMeasurementText = "保存済みの計測: なし";
            IsMeasurementStale = false;
            return;
        }

        var age = _timeProvider.GetUtcNow() - profile.MeasuredAtUtc;
        var days = Math.Max(0, (int)age.TotalDays);
        var local = profile.MeasuredAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        IsMeasurementStale = age > StaleAfter;
        SavedMeasurementText = IsMeasurementStale
            ? $"保存済みの計測: {local}（{days} 日前）。30 日を超えています。回線が変わっていなければそのままでも構いませんが、再計測をおすすめします。"
            : $"保存済みの計測: {local}（{days} 日前）";
    }

    internal static string FormatLine(UplinkLimitState state)
    {
        if (!state.IsReadable)
        {
            return "回線: 取得不可";
        }

        var line = state.Line;
        if (!line.IsIdentified)
        {
            return "回線: 特定できません（既定経路またはゲートウェイの MAC を取得できませんでした）";
        }

        return line.Kind == UplinkAdapterKind.Virtual
            ? $"回線: {line.AdapterName}（仮想アダプターのため計測・適用できません）"
            : $"回線: {line.AdapterName}";
    }

    internal static string FormatLimit(UplinkLimitState state)
    {
        if (!state.IsReadable)
        {
            return "上限: 取得不可（管理者権限で起動しているか確認してください）";
        }

        if (state.OtherPolicyNames.Count > 0)
        {
            return $"上限: 他の QoS ポリシーあり（{string.Join("、", state.OtherPolicyNames)}）。計測・適用はできません";
        }

        var text = state.LimitPolicy is { } limit
            ? $"上限: 適用中 {FormatMbps(limit.ThrottleBitsPerSecond)}"
            : "上限: なし";
        return state.TrialPolicy is null ? text : text + "（計測用の一時ポリシーが残っています）";
    }

    internal static string FormatMbps(long bitsPerSecond) =>
        string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1_000_000.0:F1} Mbps");

    internal static string FormatProgress(UplinkMeasurementProgress progress)
    {
        var phase = progress.Phase switch
        {
            UplinkMeasurementPhase.Preparing => "準備",
            UplinkMeasurementPhase.Idle => "無負荷の測定",
            UplinkMeasurementPhase.Capacity => "回線容量の測定",
            UplinkMeasurementPhase.Search => "上限の探索",
            UplinkMeasurementPhase.Verification => "効果の検証",
            UplinkMeasurementPhase.Cleanup => "後片付け",
            _ => string.Empty,
        };
        var sentMb = progress.AppSentBytes / 1_000_000.0;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{phase}: {progress.Message}（{(int)progress.Elapsed.TotalSeconds} 秒経過・送信 {sentMb:F0} MB）");
    }

    private void ClearResult()
    {
        _proposalReport = null;
        _previousLimit = null;
        HasResult = false;
        HasProposal = false;
        HasPreviousLimit = false;
        ResultRows.Clear();
        VerdictText = string.Empty;
        ProposalText = string.Empty;
        CleanupWarningText = string.Empty;
    }

    private void ShowReport(UplinkMeasurementReport report, long? previousLimit, bool cancelRequested)
    {
        ClearResult();
        HasResult = true;
        ResultRows.Add(MakeRow("無負荷", report.Idle));
        ResultRows.Add(MakeRow("負荷・上限なし", report.Loaded));
        ResultRows.Add(MakeRow("負荷・上限あり（最後に試した値）", report.Limited));
        VerdictText = DescribeOutcome(report);
        StatusText = report.Outcome == UplinkMeasurementOutcome.Cancelled || cancelRequested
            ? "計測を中止しました。"
            : "計測が終わりました。";

        if (report is { Outcome: UplinkMeasurementOutcome.Recommended, RecommendedPolicyBitsPerSecond: { } rec })
        {
            _proposalReport = report;
            HasProposal = true;
            var share = report.CapacityBitsPerSecond is { } c and > 0 ? 100.0 * rec / c : 0;
            ProposalText = string.Create(
                CultureInfo.InvariantCulture,
                $"提案する上限: {FormatMbps(rec)}（回線容量の約 {share:F0}%）。{PolicyScopeNotice}");
            if (previousLimit is { } previous)
            {
                // 計測のために外した適用済みの上限は、計測では復元されない。元の値と戻す手段を示す。
                _previousLimit = previous;
                HasPreviousLimit = true;
                PreviousLimitButtonText = $"計測前の上限 {FormatMbps(previous)} へ戻す";
                ProposalText += $" 計測前に適用していた上限 {FormatMbps(previous)} は、計測のために外したままで、現在は上限なしです。"
                                + "この提案を適用しない場合は、「計測前の上限へ戻す」を押してください。";
            }
        }

        if (!report.CleanupSucceeded)
        {
            CleanupWarningText =
                "計測用の一時ポリシー（ShisuiUplinkTrial）または元の上限の復元を確認できませんでした"
                + (report.CleanupError is null ? "。" : $": {report.CleanupError}")
                + " 「現在の状態」を更新して確認し、残っていれば Shisui を再起動してください。";
        }
        else if (report.RestoredLimitBitsPerSecond is { } restored)
        {
            VerdictText += $" 計測前の上限 {FormatMbps(restored)} は元に戻しました。";
        }
    }

    private static UplinkResultRow MakeRow(string label, UplinkSegmentStats? stats) =>
        stats is null
            ? new UplinkResultRow(label, "—", "—", "—")
            : new UplinkResultRow(
                label,
                stats.RttP90Ms is { } p90 ? string.Create(CultureInfo.InvariantCulture, $"{p90:F1} ms") : "—",
                string.Create(CultureInfo.InvariantCulture, $"{stats.LossPercent:F1} %"),
                string.Create(CultureInfo.InvariantCulture, $"{stats.NicSendMeanBitsPerSecond / 1_000_000.0:F1} Mbps"));

    internal static string DescribeOutcome(UplinkMeasurementReport report)
    {
        var detail = string.IsNullOrWhiteSpace(report.Message) ? string.Empty : $"（{report.Message}）";
        return report.Outcome switch
        {
            UplinkMeasurementOutcome.Recommended => "上限をかけると、送信中の遅延が改善しました。",
            UplinkMeasurementOutcome.NoChangeNeeded => "上りを使い切っても遅延・損失が悪化しないため、制限は不要です。",
            UplinkMeasurementOutcome.NoImprovement => $"改善を確認できなかったため、上限は提案しません。{detail}",
            UplinkMeasurementOutcome.Unstable => $"無負荷の時点で回線が不安定なため、計測できませんでした。時間をおいて再試行してください。{detail}",
            UplinkMeasurementOutcome.LineBusy => $"他の通信が多いため計測を中止しました。ほかのアプリの通信を止めてから再試行してください。{detail}",
            UplinkMeasurementOutcome.TargetError => $"測定先（Cloudflare）がエラーを返したため中止しました。回線の問題ではない可能性があります。時間をおいて再試行してください。{detail}",
            UplinkMeasurementOutcome.LimitReached => $"通信量または時間の上限（2 GB・5 分）のため未完了です。適用していません。{detail}",
            UplinkMeasurementOutcome.InvalidSegment => $"測定値が安定せず、やり直しても有効な結果を得られませんでした。{detail}",
            UplinkMeasurementOutcome.RejectedOtherPolicies => $"他の QoS ポリシーがあるため計測しませんでした: {string.Join("、", report.OtherPolicyNames)}",
            UplinkMeasurementOutcome.RejectedVirtualAdapter => "既定経路が VPN などの仮想アダプターのため計測しませんでした。",
            UplinkMeasurementOutcome.RejectedLineUnknown => $"回線（既定経路・ゲートウェイの MAC）を特定できない、または計測中に変わったため中止しました。{detail}",
            UplinkMeasurementOutcome.RejectedStateUnreadable => "QoS ポリシーを読み取れないため計測しませんでした。管理者権限で起動しているか確認してください。",
            UplinkMeasurementOutcome.PolicyFailed => $"試験用ポリシーの適用または読み戻しに失敗したため中止しました。{detail}",
            UplinkMeasurementOutcome.Cancelled => "計測を中止しました。",
            UplinkMeasurementOutcome.Failed => $"計測中に想定外のエラーが起きたため中止しました。{detail}",
            _ => report.Message,
        };
    }

    public void Dispose() => Shutdown(ShutdownWaitTimeout);
}
