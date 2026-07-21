using System.Diagnostics;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>指定バイト数の受信に要した時間から、TCP ダウンロード速度を計測する。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDownloadSpeedMeasurementService : IDownloadSpeedMeasurementService
{
    private const int InterSampleDelayMs = 300;
    private static readonly TimeSpan MeasurementTimeout = TimeSpan.FromSeconds(30);

    public async Task<DownloadSpeedMeasurementResult> MeasureAsync(
        int testSizeBytes, int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        testSizeBytes = Math.Clamp(testSizeBytes, 1, WindowsBenchmarkDownloadCatalog.MaxSafeTestSizeBytes);
        sampleCount = Math.Max(1, sampleCount);

        var speedsMbps = new List<double>(sampleCount);
        string? lastError = null;

        // 呼び出し1回が1つのAuto-Tuningレベルに対応する。レベル切り替え後の新しい接続にする。
        using var client = new HttpClient();

        for (var i = 0; i < sampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(i);

            if (i > 0)
            {
                await Task.Delay(InterSampleDelayMs, ct);
            }

            // 全レベルで同じサンプル番号に同じリージョンを割り当て、地理的な条件差を揃える。
            var target = WindowsBenchmarkDownloadCatalog.Targets[i % WindowsBenchmarkDownloadCatalog.Targets.Count];
            var sample = await MeasureOnceAsync(client, target, testSizeBytes, ct);
            if (sample.Success && sample.MegabitsPerSecond is { } speedMbps)
            {
                speedsMbps.Add(speedMbps);
            }
            else
            {
                lastError = sample.ErrorMessage;
            }
        }

        var summary = WindowsAutoTuningBenchmarkMath.SummarizeMegabitsPerSecond(speedsMbps);
        return summary is { } s
            ? new DownloadSpeedMeasurementResult(true, s.Average, s.Min, s.Max, speedsMbps.Count, null)
            : new DownloadSpeedMeasurementResult(false, null, null, null, 0, lastError);
    }

    private static async Task<SingleMeasurement> MeasureOnceAsync(
        HttpClient client, WindowsBenchmarkDownloadCatalog.DownloadTarget target, int testSizeBytes, CancellationToken ct)
    {
        using var measurementCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        measurementCts.CancelAfter(MeasurementTimeout);
        var measurementCt = measurementCts.Token;

        try
        {
            using var request = WindowsBenchmarkDownloadCatalog.CreateRangeRequest(target, testSizeBytes);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, measurementCt);
            if (!response.IsSuccessStatusCode)
            {
                return new SingleMeasurement(
                    false, null, $"[{target.Name}] {WindowsBenchmarkDownloadCatalog.FormatHttpError(response)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(measurementCt);
            var buffer = new byte[81920];
            var receivedBytes = 0;
            var stopwatch = Stopwatch.StartNew();

            while (receivedBytes < testSizeBytes)
            {
                var bytesToRead = Math.Min(buffer.Length, testSizeBytes - receivedBytes);
                var read = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), measurementCt);
                if (read == 0)
                {
                    break;
                }

                receivedBytes += read;
            }

            stopwatch.Stop();
            if (receivedBytes < testSizeBytes)
            {
                return new SingleMeasurement(
                    false, null, $"[{target.Name}] ダウンロードデータが不足しています ({receivedBytes}/{testSizeBytes} bytes)");
            }

            var megabitsPerSecond = WindowsAutoTuningBenchmarkMath.CalculateMegabitsPerSecond(
                receivedBytes, stopwatch.Elapsed);
            return megabitsPerSecond is { } speed
                ? new SingleMeasurement(true, speed, null)
                : new SingleMeasurement(false, null, $"[{target.Name}] 計測時間を取得できませんでした");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SingleMeasurement(false, null, $"[{target.Name}] タイムアウトしました");
        }
        catch (HttpRequestException ex)
        {
            return new SingleMeasurement(false, null, $"[{target.Name}] {ex.Message}");
        }
    }

    private readonly record struct SingleMeasurement(
        bool Success, double? MegabitsPerSecond, string? ErrorMessage);
}
