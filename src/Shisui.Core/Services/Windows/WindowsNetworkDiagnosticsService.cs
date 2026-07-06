using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkDiagnosticsService(ICommandExecutor executor) : INetworkDiagnosticsService
{
    /// <summary>トレースルートの各ホップ ping を並列実行する際の同時実行数上限。</summary>
    private const int MaxConcurrentHopPings = 8;

    public async Task<PingResult> PingAsync(string host, int count, CancellationToken ct = default)
    {
        string arguments;
        try
        {
            arguments = WindowsPingCommandBuilder.BuildArguments(host, count);
        }
        catch (ArgumentException ex)
        {
            return PingResult.Failed(host, ex.Message);
        }

        var result = await executor.RunAsync(WindowsPingCommandBuilder.FileName, arguments, ct);

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
        string arguments;
        try
        {
            arguments = WindowsTraceRouteCommandBuilder.BuildArguments(host, maxHops);
        }
        catch (ArgumentException ex)
        {
            return new TraceRouteResult(false, host, [], ex.Message);
        }

        var routeResult = await executor.RunAsync(WindowsTraceRouteCommandBuilder.FileName, arguments, ct);

        if (!routeResult.Success)
        {
            return new TraceRouteResult(false, host, [], routeResult.StandardError);
        }

        var hopAddresses = WindowsTraceRouteParser.ParseHopAddresses(routeResult.StandardOutput);

        // 各ホップの ping は独立した宛先への測定で、逐次実行だとホップ数 (最大 maxHops) 分の PowerShell
        // プロセス起動オーバーヘッドが積み上がる (2026-07-06 /rere レビューで発見)。ただし全ホップを無制限に
        // 並列起動すると瞬間的なプロセス負荷や ICMP 一斉送信が大きくなるため、同時実行数を絞って並列化する。
        using var throttle = new SemaphoreSlim(MaxConcurrentHopPings);
        var pingTasks = hopAddresses.Select(async address =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await PingAsync(address, count: 1, ct);
            }
            finally
            {
                throttle.Release();
            }
        });
        var pings = await Task.WhenAll(pingTasks);

        var hops = new List<TraceRouteHop>(hopAddresses.Count);
        for (var i = 0; i < hopAddresses.Count; i++)
        {
            hops.Add(new TraceRouteHop(i + 1, hopAddresses[i], pings[i].AverageRoundtripMs));
        }

        return new TraceRouteResult(hops.Count > 0, host, hops, routeResult.StandardOutput);
    }
}
