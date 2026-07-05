namespace Shisui.Core.Models;

/// <summary>
/// トレースルート 1 ホップ分。中継ルーターが ICMP に応答しない場合は
/// <see cref="RoundtripMs"/> が null になる (経路自体は分かるが応答時間は不明、という通常の
/// トレースルートの挙動)。
/// </summary>
public sealed record TraceRouteHop(int HopNumber, string? Address, double? RoundtripMs);

/// <summary>
/// トレースルートの実行結果。
/// </summary>
public sealed record TraceRouteResult(
    bool Success,
    string Host,
    IReadOnlyList<TraceRouteHop> Hops,
    string RawOutput);
