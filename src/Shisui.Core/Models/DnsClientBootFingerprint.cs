namespace Shisui.Core.Models;

/// <summary>System 検査境界とは別に保存する、起動イベント 12/27 の組。</summary>
public sealed record DnsClientBootFingerprint(
    long GeneralRecordId, string GeneralXmlSha256, long BootRecordId, string BootXmlSha256)
{
    public bool IsValid => GeneralRecordId > 0 && BootRecordId > GeneralRecordId
        && IsHash(GeneralXmlSha256) && IsHash(BootXmlSha256);

    public static DnsClientBootState Compare(DnsClientBootFingerprint previous,
        DnsClientBootFingerprint current, bool previousAnchorsConfirmed)
    {
        if (!previousAnchorsConfirmed || !previous.IsValid || !current.IsValid)
            return DnsClientBootState.Unknown;
        if (previous == current) return DnsClientBootState.SameBoot;
        return current.GeneralRecordId > previous.BootRecordId
            ? DnsClientBootState.ConfirmedNewBoot : DnsClientBootState.Unknown;
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public enum DnsClientBootState { Unknown, SameBoot, ConfirmedNewBoot }

public sealed record DnsClientBootObservation(
    CommandExecutionResult Result, DnsClientBootState State, DnsClientBootFingerprint? Current);
