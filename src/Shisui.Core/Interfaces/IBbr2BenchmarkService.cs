using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>BBR2と既定の輻輳制御を同条件で比較し、開始前の5テンプレートを厳密に復元する。</summary>
public interface IBbr2BenchmarkService
{
    Task<IReadOnlyList<Bbr2BenchmarkResult>> RunAsync(
        int testSizeBytes,
        IProgress<Bbr2BenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}
