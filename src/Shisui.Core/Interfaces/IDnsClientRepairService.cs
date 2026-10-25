using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>DNS クライアントの現在状態を検査し、キャッシュの再構築後に再確認する。</summary>
public interface IDnsClientRepairService
{
    Task<DnsClientRepairBaseline> CaptureBaselineAsync(CancellationToken ct = default);

    Task<DnsClientRepairReport> RepairAndVerifyAsync(
        DnsServerSet servers,
        DnsClientRepairBaseline baseline,
        bool inspectDohEvents,
        bool deferFailedResolution,
        CancellationToken ct = default);
}
