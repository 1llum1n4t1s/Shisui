using System.Net;
using System.Net.Http.Headers;

namespace Shisui.Core.Services.Windows;

/// <summary>TCP ベンチマーク間で計測先と HTTP Range 要求の条件を揃える。</summary>
internal static class WindowsBenchmarkDownloadCatalog
{
    public const int MaxSafeTestSizeBytes = 90_000_000;

    public static IReadOnlyList<DownloadTarget> Targets { get; } =
    [
        new("Hetzner fsn1", "https://fsn1-speed.hetzner.com/100MB.bin"),
        new("Hetzner nbg1", "https://nbg1-speed.hetzner.com/100MB.bin"),
        new("Hetzner hil", "https://hil-speed.hetzner.com/100MB.bin"),
        new("Hetzner sin", "https://sin-speed.hetzner.com/100MB.bin"),
        new("Hetzner ash", "https://ash-speed.hetzner.com/100MB.bin"),
    ];

    public static HttpRequestMessage CreateRangeRequest(DownloadTarget target, int bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
        request.Headers.Range = new RangeHeaderValue(0, bytes - 1);
        return request;
    }

    public static string FormatHttpError(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
        }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        var suffix = retryAfter is { } wait
            ? wait.TotalMinutes >= 1
                ? $"、約{Math.Ceiling(wait.TotalMinutes)}分後に再試行可能"
                : $"、約{Math.Ceiling(wait.TotalSeconds)}秒後に再試行可能"
            : string.Empty;
        return $"HTTP 429 (Too Many Requests){suffix}";
    }

    internal sealed record DownloadTarget(string Name, string Url);
}
