using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 受信ウィンドウ自動調整の各レベルを実際に切り替え、TCP ダウンロード負荷中の Ping を計測して、
/// 現在の回線環境にどのレベルが合っているかを判断できるようにする (Windows 専用機能)。
/// macOS では DI 登録されず、UI 側がボタンごと非表示にする。
/// </summary>
public interface IAutoTuningBenchmarkService
{
    /// <summary>
    /// 全レベルを順に切り替えながら <paramref name="testSizeBytes"/> バイトの TCP ダウンロード負荷を発生させ、
    /// その最中の Ping をレベルごとに固定5回計測して平均値を返す。無負荷の
    /// ICMP Ping だけでは TCP 受信ウィンドウ自動調整の差を反映できないため、負荷時レイテンシとして測る。
    /// 計測開始前のレベルは完了・キャンセル・例外いずれの場合も自動的に復元される。
    /// </summary>
    Task<IReadOnlyList<AutoTuningBenchmarkResult>> RunAsync(
        int testSizeBytes,
        IProgress<AutoTuningBenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}
