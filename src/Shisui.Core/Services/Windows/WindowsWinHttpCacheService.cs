using System.ComponentModel;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsWinHttpCacheService(IWinHttpAutoProxyApi api, TimeProvider? timeProvider = null) : IWinHttpCacheService
{
    private const string Operation = "WinHttpResetAutoProxy (自動プロキシのキャッシュのみ、flags=0x0002000C)";
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _attemptGate = new(1, 1);
    private long? _lastAttemptTimestamp;
    private CommandExecutionResult? _lastAttemptResult;

    public async Task<CommandExecutionResult> ClearAutoProxyCacheAsync(CancellationToken ct = default)
    {
        // 共有ネットワーク変更ゲートは呼び出し元が保持する。ここでは API の間隔だけを直列化する。
        await _attemptGate.WaitAsync(ct);
        var trace = new CommandExecutionTrace(Operation);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_lastAttemptTimestamp is { } previous && _clock.GetElapsedTime(previous) < MinimumInterval)
            {
                var reason = "直前の試行から30秒以内で省略。今回はキャッシュ消去 API を呼び出していません。";
                if (_lastAttemptResult is { Success: true })
                    return trace.Complete(new(true, Operation, 0,
                        $"前回 API 受付済み。{reason} 実際のキャッシュ消去は測定していません。", string.Empty));
                if (_lastAttemptResult is { Success: false } failed)
                    reason += $" 前回も未完了です: {failed.StandardError}";
                return trace.Complete(new(false, Operation, -1, string.Empty, reason));
            }

            var error = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _lastAttemptResult = null;
                try
                {
                    return api.ResetAutoProxy(WindowsWinHttpAutoProxyApi.CacheOnlyFlags);
                }
                finally
                {
                    // セッション開始に時間がかかっても、実際のリセット呼び出し同士を30秒以上離す。
                    _lastAttemptTimestamp = _clock.GetTimestamp();
                }
            }, ct);

            _lastAttemptResult = error == 0
                ? new(true, Operation, 0,
                    "自動プロキシキャッシュの消去要求を API が受け付けました。実際のキャッシュ消去は測定していません。保存済みプロキシ設定は変更していません。",
                    string.Empty)
                : new(false, Operation, unchecked((int)error), string.Empty,
                    $"WinHTTP の自動プロキシキャッシュ消去要求が失敗しました (Windows エラー {error})。");
            ct.ThrowIfCancellationRequested();
            return trace.Complete(_lastAttemptResult);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                       or Win32Exception or PlatformNotSupportedException)
        {
            _lastAttemptResult = new(false, Operation,
                ex is Win32Exception native ? native.NativeErrorCode : -1, string.Empty,
                $"WinHTTP の自動プロキシキャッシュ消去 API を実行できませんでした: {ex.Message}");
            return trace.Complete(_lastAttemptResult);
        }
        catch (Exception ex)
        {
            trace.Interrupted(ex);
            throw;
        }
        finally
        {
            _attemptGate.Release();
        }
    }
}
