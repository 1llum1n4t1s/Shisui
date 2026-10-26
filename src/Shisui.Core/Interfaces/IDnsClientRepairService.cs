using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>DNS クライアントの読取り検査と動作確認を行う。</summary>
public interface IDnsClientRepairService
{
    Task<DnsClientRepairBaseline> CaptureBaselineAsync(CancellationToken ct = default);

    Task<DnsClientBootObservation> CheckBootAsync(DnsClientBootFingerprint? previous, CancellationToken ct = default);

    Task<DnsClientRepairReport> RepairAndVerifyAsync(
        DnsServerSet servers,
        DnsClientRepairBaseline baseline,
        bool inspectDohEvents,
        CancellationToken ct = default);
}
