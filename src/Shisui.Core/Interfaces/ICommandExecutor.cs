using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 外部プロセスを実行する最下層の抽象。Windows はそのまま起動、
/// macOS は AppleScript 経由の管理者権限昇格をここでラップする。
/// </summary>
/// <remarks>
/// arguments は組み立て済みの 1 本の文字列で渡す (配列にしない)。
/// netsh は CommandLineToArgvW 標準の argv 分割ではなく独自の生コマンドライン再パースを行い、
/// name="アダプタ名" のような引用符をそのまま要求するため、.NET の ArgumentList 自動エスケープを通すと
/// クォートがバックスラッシュでエスケープされて netsh 側の解釈と噛み合わなくなる。
/// 呼び出し側 (各 CommandBuilder) が対象コマンドの流儀に合わせて正しくクォートした文字列を渡す。
/// </remarks>
public interface ICommandExecutor
{
    Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default);
}
