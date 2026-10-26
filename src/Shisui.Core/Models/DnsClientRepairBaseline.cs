namespace Shisui.Core.Models;

/// <summary>変更開始前の System ログ境界。実行中のみ保持し、設定へ保存しない。</summary>
public sealed record DnsClientRepairBaseline(CommandExecutionResult Result, long? RecordId, string? XmlSha256)
{
    public DnsClientBootFingerprint? BootFingerprint { get; init; }
}
