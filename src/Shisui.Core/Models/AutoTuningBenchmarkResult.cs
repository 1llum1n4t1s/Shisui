using Shisui.Core.Interfaces;

namespace Shisui.Core.Models;

/// <summary>
/// 受信ウィンドウ自動調整 (auto-tuning) の 1 レベル分のベンチマーク結果。回線状況によるブレを均すため
/// TCP ダウンロード速度を複数回計測した平均値であり、<see cref="MinMbps"/>/<see cref="MaxMbps"/> でその
/// ブレの幅も分かるようにしている。
/// </summary>
public sealed record AutoTuningBenchmarkResult(
    AutoTuningLevel Level,
    bool Success,
    double? AverageMbps,
    double? MinMbps,
    double? MaxMbps,
    int SampleCount,
    string? ErrorMessage);

/// <summary>ベンチマーク実行中の進捗通知 (現在計測中のレベルと、全レベル×全サンプル通算で何回目か)。</summary>
public sealed record AutoTuningBenchmarkProgress(AutoTuningLevel Level, int CompletedCount, int TotalCount);
