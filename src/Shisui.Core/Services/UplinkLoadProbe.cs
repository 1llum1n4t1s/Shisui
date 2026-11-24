using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// 上り負荷 (HTTP/1.1 の POST を接続数ぶん並列に繰り返す) と、NIC 送信統計 (1 秒ごと)・ICMP (10Hz) の観測。
/// 統計・判定は <see cref="UplinkLimitMath"/> が行い、ここは生データだけを返す。
/// 送信バイトはリクエスト本文のストリームへの書き込み完了で数える。書き込み完了はカーネル/TLS の送信バッファに
/// 入った時点なので、実際に送られた量より最大で「送信バッファ × 接続数」だけ先行する。
/// これを抑えるため接続ごとの SO_SNDBUF を <see cref="SendBufferBytes"/> に固定する (超高速回線では接続あたりの上限になる)。
/// 先行はアプリ送信が NIC 送信を上回る側にだけ出るため、不一致判定 (NIC − アプリ) は保守側に倒れる。
/// </summary>
public sealed class UplinkLoadProbe : IUplinkLoadProbe
{
    /// <summary>既定の負荷先。</summary>
    public static readonly Uri DefaultTarget = new("https://speed.cloudflare.com/__up");

    /// <summary>ICMP を送ってよい唯一の宛先。</summary>
    public const string AllowedPingTarget = "1.1.1.1";

    private const int PostBodyBytes = 8 * 1024 * 1024;
    private const int WriteChunkBytes = 16 * 1024;
    private const int SendBufferBytes = 512 * 1024;
    private const int PingIntervalMs = 100;
    private const int PingTimeoutMs = 1000;
    private const int SampleIntervalMs = 1000;

    private readonly Uri _target;

    /// <param name="target">負荷先。省略時は <see cref="DefaultTarget"/> (テストでローカルの受け口に向けるために変更できる)。</param>
    public UplinkLoadProbe(Uri? target = null)
    {
        _target = target ?? DefaultTarget;
    }

    public async Task<UplinkProbeResult> RunAsync(UplinkProbeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.PingTarget, AllowedPingTarget, StringComparison.Ordinal))
        {
            throw new ArgumentException($"ICMP は {AllowedPingTarget} 以外へ送信できません。", nameof(request));
        }

        if (request.Duration <= TimeSpan.Zero || request.ConnectionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "区間の長さは正、接続数は 0 以上にしてください。");
        }

        ct.ThrowIfCancellationRequested();
        var session = new ProbeSession(request, _target);
        return await session.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>1 区間分の状態。区間ごとに作り、終了時に全て片付ける。</summary>
    private sealed class ProbeSession
    {
        private readonly UplinkProbeRequest _request;
        private readonly Uri _target;
        private readonly Stopwatch _clock = new();
        private readonly List<UplinkProbeInterval> _intervals = [];
        private readonly List<UplinkPingSample> _pings = [];
        private readonly Lock _pingLock = new();
        private long _appSentBytes;
        private CancellationTokenSource? _stop;
        private int _status = (int)UplinkProbeStatus.Completed;
        private string? _detail;
        private int? _httpStatus;

        public ProbeSession(UplinkProbeRequest request, Uri target)
        {
            _request = request;
            _target = target;
        }

        public async Task<UplinkProbeResult> RunAsync(CancellationToken ct)
        {
            var nic = FindNic(_request.AdapterGuid);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _stop = stop;
            using var handler = CreateHandler(_request.ConnectionCount);
            using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

            // 負荷と NIC 採取は期限で止める。Ping は送信済みぶんの応答 (最大 1 秒強) まで待つため、停止用とは別のトークンにする。
            // Ping を即時に打ち切るのは、外部キャンセル・測定先エラー・総量上限のときだけ。
            using var pingAbort = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = new List<Task>();
            var completed = false;
            try
            {
                _clock.Start();
                var token = stop.Token;
                for (var i = 0; i < _request.ConnectionCount; i++)
                {
                    tasks.Add(Task.Run(() => LoadWorkerAsync(client, stop, token), CancellationToken.None));
                }

                tasks.Add(Task.Run(() => PingLoopAsync(pingAbort.Token), CancellationToken.None));
                var sampler = Task.Run(() => SampleLoopAsync(nic, token), CancellationToken.None);
                tasks.Add(sampler);

                // 期限まで (または異常・キャンセルまで) 待つ。サンプラーは期限で自然に終わる。
                await sampler.ConfigureAwait(false);
                completed = true;
            }
            finally
            {
                if (!completed || ct.IsCancellationRequested || (UplinkProbeStatus)Volatile.Read(ref _status) != UplinkProbeStatus.Completed)
                {
                    await pingAbort.CancelAsync().ConfigureAwait(false);
                }

                await StopAllAsync(stop, tasks).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            return BuildResult();
        }

        private async Task StopAllAsync(CancellationTokenSource stop, List<Task> tasks)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            // 各タスクは自分でキャンセルを吸収するため、ここで例外が出るのは想定外の失敗だけ。
            // 例外を握り潰さず、後始末 (全タスクの完了待ち) を終えてから最初の失敗を伝える。
            var all = Task.WhenAll(tasks);
            try
            {
                await all.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // キャンセル由来は ProbeSession.RunAsync 側が ct で判定して投げ直す。
            }
        }

        private async Task SampleLoopAsync(NetworkInterface nic, CancellationToken token)
        {
            var duration = (long)_request.Duration.TotalMilliseconds;
            var previous = ReadNic(nic);
            var previousAt = _clock.ElapsedMilliseconds;
            var previousApp = Interlocked.Read(ref _appSentBytes);
            var next = 0L;

            while (next < duration)
            {
                next = Math.Min(next + SampleIntervalMs, duration);
                var wait = next - _clock.ElapsedMilliseconds;
                if (wait > 0)
                {
                    try
                    {
                        await Task.Delay((int)wait, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                var current = ReadNic(nic);
                var now = _clock.ElapsedMilliseconds;
                var app = Interlocked.Read(ref _appSentBytes);
                _intervals.Add(new UplinkProbeInterval(
                    now - previousAt,
                    Math.Max(0, current.Sent - previous.Sent),
                    Math.Max(0, current.Received - previous.Received),
                    Math.Max(0, app - previousApp)));
                (previous, previousAt, previousApp) = (current, now, app);
            }
        }

        private async Task PingLoopAsync(CancellationToken token)
        {
            var duration = (long)_request.Duration.TotalMilliseconds;
            var address = IPAddress.Parse(AllowedPingTarget);
            var outstanding = new List<Task>();
            var tick = 0L;
            try
            {
                while (tick < duration)
                {
                    var wait = tick - _clock.ElapsedMilliseconds;
                    if (wait > 0)
                    {
                        await Task.Delay((int)wait, token).ConfigureAwait(false);
                    }

                    outstanding.Add(SendOnePingAsync(address, _clock.ElapsedMilliseconds, token));
                    tick += PingIntervalMs;
                }

                // 期限までに送った分の応答 (最大 1 秒) を待つ。
                await Task.WhenAll(outstanding).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 送信中の Ping は同じトークンで打ち切られ、例外にせず戻る。全て終わるまで待つ。
                await Task.WhenAll(outstanding).ConfigureAwait(false);
            }
        }

        private async Task SendOnePingAsync(IPAddress address, long offsetMs, CancellationToken token)
        {
            double? rtt = null;
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(PingTimeoutMs), null, null, token)
                    .ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    rtt = reply.RoundtripTime;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return; // 打ち切られた標本は損失にも数えない。
            }
            catch (PingException)
            {
                // 送信失敗 (経路なしなど) は損失として数える。
            }
            catch (SocketException)
            {
            }

            lock (_pingLock)
            {
                _pings.Add(new UplinkPingSample(offsetMs, rtt));
            }
        }

        private async Task LoadWorkerAsync(HttpClient client, CancellationTokenSource stop, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, _target)
                    {
                        Version = HttpVersion.Version11,
                        VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                        Content = new CountingContent(this),
                    };
                    using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token)
                        .ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        Fail(UplinkProbeStatus.TargetError, $"HTTP {(int)response.StatusCode}", (int)response.StatusCode);
                        await stop.CancelAsync().ConfigureAwait(false);
                        return;
                    }

                    await response.Content.CopyToAsync(Stream.Null, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (token.IsCancellationRequested
                && ex is OperationCanceledException or HttpRequestException or IOException or SocketException)
            {
                // 期限・上限・他の接続の失敗・呼び出し側のキャンセルによる停止。停止中に出た通信エラーは測定先の失敗にしない。
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or OperationCanceledException)
            {
                Fail(UplinkProbeStatus.TargetError, $"{ex.GetType().Name}: {ex.Message}", null);
                await stop.CancelAsync().ConfigureAwait(false);
            }
        }

        /// <summary>CountingContent から呼ばれる。送信済みを加算し、総量上限なら停止を要求する。</summary>
        public bool AddSent(int bytes)
        {
            var total = Interlocked.Add(ref _appSentBytes, bytes);
            if (total < _request.MaxAppSentBytes)
            {
                return true;
            }

            Fail(UplinkProbeStatus.ByteLimitReached, "総送信量の上限に到達", null);
            _stop?.Cancel();
            return false;
        }

        /// <summary>最初の失敗だけを残す (TargetError は ByteLimitReached より優先)。</summary>
        private void Fail(UplinkProbeStatus status, string detail, int? httpStatus)
        {
            lock (_pingLock)
            {
                var current = (UplinkProbeStatus)_status;
                if (current == UplinkProbeStatus.TargetError
                    || (current == UplinkProbeStatus.ByteLimitReached && status != UplinkProbeStatus.TargetError))
                {
                    return;
                }

                _status = (int)status;
                _detail = detail;
                _httpStatus = httpStatus;
            }
        }

        private UplinkProbeResult BuildResult()
        {
            lock (_pingLock)
            {
                return new UplinkProbeResult(
                    (UplinkProbeStatus)_status,
                    _detail,
                    _httpStatus,
                    _intervals.ToArray(),
                    _pings.OrderBy(static p => p.OffsetMilliseconds).ToArray(),
                    Interlocked.Read(ref _appSentBytes));
            }
        }

        private static NetworkInterface FindNic(string adapterGuid)
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.Id.Equals(adapterGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return nic;
                }
            }

            throw new InvalidOperationException($"NIC {adapterGuid} が見つかりません (回線が変わった可能性があります)。");
        }

        private static (long Sent, long Received) ReadNic(NetworkInterface nic)
        {
            // GetIPStatistics は呼び出し時点の値を返す (キャッシュしない)。
            var statistics = nic.GetIPStatistics();
            return (statistics.BytesSent, statistics.BytesReceived);
        }
    }

    private static SocketsHttpHandler CreateHandler(int connectionCount) => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false, // 3xx は成功以外として測定先エラーにする。
        MaxConnectionsPerServer = Math.Max(1, connectionCount),
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = ConnectWithFixedSendBufferAsync,
        // 証明書検証は既定のまま (SslOptions を変更しない)。
    };

    private static async ValueTask<Stream> ConnectWithFixedSendBufferAsync(
        SocketsHttpConnectionContext context, CancellationToken token)
    {
        // LingerState (有効, 0 秒) は切断時に未送信のバッファを破棄する (停止時の残りが次の区間へ流れ込まないように)。
        // 接続の使い回し (応答受信後の再利用) には影響しない。
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            SendBufferSize = SendBufferBytes,
            LingerState = new LingerOption(true, 0),
        };
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>8MB の 0 埋め本文。書き込み完了ごとに送信済みバイトを数え、上限に達したら書き込みを止める。</summary>
    private sealed class CountingContent : HttpContent
    {
        private static readonly byte[] Chunk = new byte[WriteChunkBytes];
        private readonly ProbeSession _session;

        public CountingContent(ProbeSession session)
        {
            _session = session;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = PostBodyBytes;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var remaining = PostBodyBytes;
            while (remaining > 0)
            {
                var size = Math.Min(remaining, Chunk.Length);
                await stream.WriteAsync(Chunk.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
                remaining -= size;
                if (!_session.AddSent(size))
                {
                    throw new OperationCanceledException("総送信量の上限に到達しました。");
                }
            }
        }
    }
}
