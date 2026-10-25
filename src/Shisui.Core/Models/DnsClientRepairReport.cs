namespace Shisui.Core.Models;

/// <summary>DNS クライアントの検査結果。再起動後の確認待ちも成功には含めない。</summary>
public sealed record DnsClientRepairReport(CommandExecutionResult Result, bool VerificationDeferred);
