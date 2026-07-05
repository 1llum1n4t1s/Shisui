using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacNetworkDiagnosticsService(ICommandExecutor executor) : INetworkDiagnosticsService
{
    public async Task<PingResult> PingAsync(string host, int count, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(
            MacPingCommandBuilder.FileName,
            MacPingCommandBuilder.BuildArguments(host, count),
            ct);

        // ping は 1 件でも応答があれば ExitCode 0 だが、全滅時は非 0 で終了する。
        // どちらの場合も出力に統計行が残るため、成功可否に関わらずまずパースを試みる。
        return MacPingResultParser.Parse(
            result.StandardOutput.Length > 0 ? result.StandardOutput : result.StandardError,
            host,
            count);
    }

    public async Task<TraceRouteResult> TraceRouteAsync(string host, int maxHops, CancellationToken ct = default)
    {
        var result = await executor.RunAsync(
            MacTraceRouteCommandBuilder.FileName,
            MacTraceRouteCommandBuilder.BuildArguments(host, maxHops),
            ct);

        var output = result.StandardOutput.Length > 0 ? result.StandardOutput : result.StandardError;
        var hops = MacTraceRouteParser.Parse(output);
        return new TraceRouteResult(hops.Count > 0, host, hops, output);
    }
}
