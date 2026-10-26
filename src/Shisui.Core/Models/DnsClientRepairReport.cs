namespace Shisui.Core.Models;

/// <summary>Result は実行済み検査の結果。DNS 動作確認の状態は別に保持する。</summary>
public sealed record DnsClientRepairReport(CommandExecutionResult Result, DnsClientVerificationStatus Status);

public enum DnsClientVerificationStatus { Unknown, Verified, Failed }
