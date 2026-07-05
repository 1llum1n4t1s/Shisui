using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkDiagnosticsService(ICommandExecutor executor) : INetworkDiagnosticsService
{
    public async Task<PingResult> PingAsync(string host, int count, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(
            WindowsPingCommandBuilder.FileName,
            WindowsPingCommandBuilder.BuildArguments(host, count),
            ct);

        return result.Success
            ? WindowsPingResultParser.Parse(result.StandardOutput, host, count)
            : PingResult.Failed(host, result.StandardError);
    }

    /// <summary>
    /// 経路 (ホップ IP) を <c>Test-NetConnection -TraceRoute</c> で取得し、各ホップの往復時間は
    /// <see cref="PingAsync"/> を 1 回ずつ呼んで個別に求める (tracert.exe のロケール依存テキストを
    /// パースしないための設計。中継ルーターが ICMP に応答しない場合、そのホップの往復時間は null になる
    /// が、これは通常のトレースルートでも起きる挙動)。
    /// </summary>
    public async Task<TraceRouteResult> TraceRouteAsync(string host, int maxHops, CancellationToken ct = default)
    {
        var routeResult = await executor.RunAsync(
            WindowsTraceRouteCommandBuilder.FileName,
            WindowsTraceRouteCommandBuilder.BuildArguments(host, maxHops),
            ct);

        if (!routeResult.Success)
        {
            return new TraceRouteResult(false, host, [], routeResult.StandardError);
        }

        var hopAddresses = WindowsTraceRouteParser.ParseHopAddresses(routeResult.StandardOutput);
        var hops = new List<TraceRouteHop>(hopAddresses.Count);

        for (var i = 0; i < hopAddresses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var address = hopAddresses[i];
            var ping = await PingAsync(address, count: 1, ct);
            hops.Add(new TraceRouteHop(i + 1, address, ping.AverageRoundtripMs));
        }

        return new TraceRouteResult(hops.Count > 0, host, hops, routeResult.StandardOutput);
    }
}
