using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Serialization;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsIpStackPlatform(ICommandExecutor executor, string? recoveryDirectory = null) : IIpStackPlatform
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);
    private const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private readonly string _recoveryDirectory = Path.GetFullPath(recoveryDirectory ?? Path.Combine(AppPaths.AppDataDirectory, "ip-stack-recovery"));
    private readonly SemaphoreSlim _restoreLock = new(1, 1);

    public async Task<string> GetBootIdAsync(CancellationToken ct = default)
    {
        var result = await RunReadAsync(WindowsIpStackResetCommandBuilder.BuildBootIdArguments(), ct);
        var bootId = result.StandardOutput.Trim();
        if (!DateTimeOffset.TryParse(bootId, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            throw new InvalidDataException("Windows の起動識別子を取得できませんでした。");
        return bootId;
    }

    public async Task<IpStackSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        IpStackSnapshot? snapshot = null;
        foreach (var section in WindowsIpStackResetCommandBuilder.CaptureSections)
        {
            var result = await RunReadAsync(WindowsIpStackResetCommandBuilder.BuildCaptureArguments(section), ct);
            var part = JsonSerializer.Deserialize(result.StandardOutput.Trim(), ShisuiJsonContext.Default.IpStackSnapshot)
                ?? throw new InvalidDataException("IP スタックの取得結果が null です。");
            if (part.SchemaVersion != 1 || string.IsNullOrWhiteSpace(part.BootId) || part.Adapters is null)
                throw new InvalidDataException("IP スタックの取得形式が不正です。");
            if (snapshot is null)
            {
                snapshot = part;
                continue;
            }
            if (snapshot.BootId != part.BootId || !Equal(AdapterIdentities(snapshot), AdapterIdentities(part)))
                throw new InvalidDataException("退避中に Windows の起動状態または NIC 構成が変わりました。");
            if (section == "Client")
            {
                snapshot.Adapters = part.Adapters;
                snapshot.Dns = part.Dns;
            }
        }
        if (snapshot is null)
            throw new InvalidDataException("IP スタックを取得できませんでした。");
        Validate(snapshot);
        // reset 開始後に引数長・保存先・payload サイズの制約が判明することを防ぐ。
        var payloadPath = Path.Combine(_recoveryDirectory, "restore-payload.json");
        ValidateRecoveryPath(payloadPath);
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot, ShisuiJsonContext.Default.IpStackSnapshot).Length > MaximumPayloadBytes)
            throw new InvalidDataException("IP スタックの復元ファイルが上限を超えました。");
        foreach (var section in WindowsIpStackResetCommandBuilder.RestoreSections)
            _ = WindowsIpStackResetCommandBuilder.BuildRestoreArguments(payloadPath, new string('0', 64), section);
        return snapshot;
    }

    public Task<CommandExecutionResult> ResetIpv4Async(CancellationToken ct = default) =>
        RunMutationAsync("netsh", "interface ipv4 reset", ct);

    public Task<CommandExecutionResult> ResetIpv6Async(CancellationToken ct = default) =>
        RunMutationAsync("netsh", "interface ipv6 reset", ct);

    public async Task<CommandExecutionResult> RestoreAsync(IpStackSnapshot snapshot, CancellationToken ct = default)
    {
        Validate(snapshot);
        await _restoreLock.WaitAsync(ct);
        try
        {
            ValidateRecoveryPath(_recoveryDirectory);
            Directory.CreateDirectory(_recoveryDirectory);
            ValidateRecoveryPath(_recoveryDirectory);
            var path = Path.Combine(_recoveryDirectory, "restore-payload.json");
            ValidateRecoveryPath(path);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, ShisuiJsonContext.Default.IpStackSnapshot);
            if (bytes.Length > MaximumPayloadBytes)
                throw new InvalidDataException("IP スタックの復元ファイルが上限を超えました。");
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await file.WriteAsync(bytes, ct);
                await file.FlushAsync(ct);
                file.Flush(flushToDisk: true);
            }
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            // 全段階の構築と長さ検証を、最初の OS 変更より先に済ませる。
            var commands = WindowsIpStackResetCommandBuilder.RestoreSections
                .Select(section => (Section: section, Arguments: WindowsIpStackResetCommandBuilder.BuildRestoreArguments(path, hash, section)))
                .ToArray();
            var results = new List<CommandExecutionResult>();
            foreach (var command in commands)
            {
                var result = await RunMutationAsync("powershell", command.Arguments, ct);
                results.Add(result with { CommandLine = "IP スタック復元: " + command.Section });
                if (result.RequiresOperationAbort)
                    break;
            }
            if (!results.Any(result => result.RequiresOperationAbort))
            {
                try
                {
                    // 製品の固定一時 payload。子の終了が確認できた場合だけ消し、原本は journal で保持する。
                    ValidateRecoveryPath(path);
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    results.Add(new CommandExecutionResult(false, "復元用一時ファイルの清掃", -1, string.Empty, ex.Message));
                }
            }
            return new CommandExecutionResult(results.All(result => result.Success), "IP スタック設定を復元",
                results.FirstOrDefault(result => !result.Success)?.ExitCode ?? 0,
                string.Join(Environment.NewLine, results.Select(result => result.StandardOutput)),
                string.Join(Environment.NewLine, results.Where(result => !result.Success).Select(result => result.CommandLine + ": " + result.StandardError)))
            {
                RequiresOperationAbort = results.Any(result => result.RequiresOperationAbort),
            };
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    public bool Matches(IpStackSnapshot expected, IpStackSnapshot actual, bool afterReboot = false)
    {
        try
        {
            Validate(expected);
            Validate(actual);
            if (!Equal(AdapterStates(expected), AdapterStates(actual)))
                return false;
            if (!Equal(InterfaceStates(expected, afterReboot), InterfaceStates(actual, afterReboot)))
                return false;
            if (!Equal(AddressStates(expected, afterReboot), AddressStates(actual, afterReboot)))
                return false;
            if (!Equal(RouteStates(expected, afterReboot), RouteStates(actual, afterReboot)))
                return false;
            if (!Equal(DnsStates(expected), DnsStates(actual)))
                return false;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private async Task<CommandExecutionResult> RunReadAsync(string arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        CommandExecutionResult result;
        try
        {
            result = await executor.RunAsync("powershell", arguments, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("IP スタック設定の取得が時間内に終わらなかったため、リセットを開始しません。");
        }
        if (!result.Success || result.RequiresOperationAbort)
            throw new InvalidOperationException("IP スタック設定を取得できないため、リセットを開始しません: " + result.StandardError);
        if (Encoding.UTF8.GetByteCount(result.StandardOutput) > MaximumPayloadBytes)
            throw new InvalidDataException("IP スタック設定の取得結果が上限を超えました。");
        return result;
    }

    private async Task<CommandExecutionResult> RunMutationAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            var result = await executor.RunAsync(fileName, arguments, timeout.Token);
            return result.ExitCode < 0 ? result with { RequiresOperationAbort = true } : result;
        }
        catch (OperationCanceledException)
        {
            return new CommandExecutionResult(false, "IP スタック操作", -1, string.Empty,
                "操作の終了を確認できませんでした。後続の変更を停止しました。PC を再起動してから復元してください。")
            { RequiresOperationAbort = true };
        }
        catch (Exception ex)
        {
            // 実行開始後の例外では子の生存を否定できないので、別の変更処理を競合実行しない。
            return new CommandExecutionResult(false, "IP スタック操作", -1, string.Empty,
                "操作の終了を確認できませんでした。PC を再起動してから復元してください: " + ex.Message)
            { RequiresOperationAbort = true };
        }
    }

    private static void ValidateRecoveryPath(string path)
    {
        var current = Path.GetFullPath(path);
        if (current == Path.GetPathRoot(current))
            throw new InvalidDataException("復元ファイルの保存先がドライブ直下です。");
        while (current is not null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("復元ファイルの保存先に reparse point が含まれます。");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // これから作成するパスでも、既存の親階層はすべて確認する。
            }
            current = Path.GetDirectoryName(current);
        }
    }

    private static readonly HashSet<string> InterfaceSettingNames = new(StringComparer.Ordinal)
    {
        "Dhcp", "AutomaticMetric", "InterfaceMetric", "NlMtuBytes", "RouterDiscovery", "Forwarding", "Advertising",
        "WeakHostSend", "WeakHostReceive", "IgnoreDefaultRoutes", "ManagedAddressConfiguration", "OtherStatefulConfiguration",
        "NeighborUnreachabilityDetection", "BaseReachableTimeMs", "RetransmitTimeMs", "DadTransmits", "DadRetransmitTimeMs",
        "AdvertisedRouterLifetime", "AdvertiseDefaultRoute", "CurrentHopLimit", "ForceArpNdWolPattern", "DirectedMacWolPattern", "EcnMarking", "ClampMss",
    };

    private static void Validate(IpStackSnapshot s)
    {
        if (s is null || s.SchemaVersion != 1 || !DateTimeOffset.TryParse(s.BootId, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
            || s.CapturedAtUtc == default || s.Adapters is null || s.Interfaces is null || s.Addresses is null || s.Routes is null
            || s.Dns is null)
            throw new InvalidDataException("IP スタックの退避情報が欠落しているか、形式が不正です。");
        if (s.Adapters.Count is 0 or > 4096 || s.Interfaces.Count == 0)
            throw new InvalidDataException("IP スタックの退避情報の対象数が不正です。");
        var adapters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in s.Adapters)
        {
            if (a is null || !adapters.Add(a.AdapterId) || (a.AdapterId != "loopback" && (!Guid.TryParseExact(a.AdapterId, "D", out var id) || id == Guid.Empty))
                || a.InterfaceIndex <= 0 || a.Description is null || a.PnpDeviceId is null || a.StaticWinsServers is null
                || a.NetbiosOptions > 2 || a.StaticWinsServers.Count > 2)
                throw new InvalidDataException("NIC の退避情報が不正です。");
            foreach (var server in a.StaticWinsServers)
                ValidateIp(server, 2);
        }
        var interfaceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in s.Interfaces)
        {
            if (i is null || !adapters.Contains(i.AdapterId) || !ValidFamily(i.AddressFamily) || !ValidStore(i.PolicyStore)
                || !interfaceKeys.Add(Key(i.AdapterId, i.AddressFamily, i.PolicyStore)))
                throw new InvalidDataException("IP interface の退避情報が不正です。");
            ValidateSettings(i.Settings, InterfaceSettingNames);
            if (i.PolicyStore == "ActiveStore")
                foreach (var required in new[] { "Dhcp", "AutomaticMetric", "InterfaceMetric", "NlMtuBytes" })
                    if (!i.Settings.Any(setting => setting.Name == required))
                        throw new InvalidDataException("IP interface の必須設定がありません。");
        }
        var addressKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in s.Addresses)
        {
            if (a is null || !adapters.Contains(a.AdapterId) || !ValidStore(a.PolicyStore) || !ValidFamily(a.AddressFamily)
                || !interfaceKeys.Contains(Key(a.AdapterId, a.AddressFamily, a.PolicyStore)) || a.Type is not ("Unicast" or "Anycast")
                || a.PrefixLength < 0 || a.PrefixLength > (a.AddressFamily == 2 ? 32 : 128))
                throw new InvalidDataException("静的 IP の退避情報が不正です。");
            ValidateIp(a.Address, a.AddressFamily);
            if (!addressKeys.Add(Key(a.AdapterId, a.AddressFamily, a.PolicyStore, NormalizeIp(a.Address))))
                throw new InvalidDataException("静的 IP の退避情報が重複しています。");
        }
        var routeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in s.Routes)
        {
            if (r is null || !adapters.Contains(r.AdapterId) || !ValidStore(r.PolicyStore) || !ValidFamily(r.AddressFamily)
                || !interfaceKeys.Contains(Key(r.AdapterId, r.AddressFamily, r.PolicyStore)) || r.Protocol != "NetMgmt" || r.Publish is not ("No" or "Age" or "Yes"))
                throw new InvalidDataException("静的 route の退避情報が不正です。");
            ValidatePrefix(r.DestinationPrefix, r.AddressFamily);
            ValidateIp(r.NextHop, r.AddressFamily);
            if (!routeKeys.Add(Key(r.AdapterId, r.AddressFamily, r.PolicyStore, NormalizePrefix(r.DestinationPrefix), NormalizeIp(r.NextHop))))
                throw new InvalidDataException("静的 route の退避情報が重複しています。");
        }
        var dnsKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in s.Dns)
        {
            if (d is null || !adapters.Contains(d.AdapterId) || !ValidFamily(d.AddressFamily) || !dnsKeys.Add(Key(d.AdapterId, d.AddressFamily))
                || d.ServerAddresses is null || (!d.Automatic && d.ServerAddresses.Count == 0) || d.ConnectionSpecificSuffix is null)
                throw new InvalidDataException("DNS の退避情報が不正です。");
            foreach (var server in d.ServerAddresses)
                ValidateIp(server, d.AddressFamily);
        }
        foreach (var i in s.Interfaces.Where(i => i.AdapterId != "loopback" && i.PolicyStore == "ActiveStore"))
            if (!dnsKeys.Contains(Key(i.AdapterId, i.AddressFamily)))
                throw new InvalidDataException("IP interface の DNS 退避情報がありません。");
    }

    private static void ValidateSettings(List<IpStackSetting> settings, HashSet<string> allowed)
    {
        if (settings is null)
            throw new InvalidDataException("IP 設定値が欠落しています。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            if (setting is null || !allowed.Contains(setting.Name) || !names.Add(setting.Name) || string.IsNullOrWhiteSpace(setting.Value)
                || setting.Value.Length > 256 || setting.Value.IndexOf('\0') >= 0
                || (setting.IsTimeSpan && !TimeSpan.TryParseExact(setting.Value, "c", CultureInfo.InvariantCulture, out _)))
                throw new InvalidDataException("IP 設定値の形式が不正です。");
        }
    }

    private static bool ValidFamily(int family) => family is 2 or 23;
    private static bool ValidStore(string store) => store is "ActiveStore" or "PersistentStore";
    private static void ValidateIp(string address, int family)
    {
        if (!IPAddress.TryParse(address, out var ip) || (int)ip.AddressFamily != family)
            throw new InvalidDataException("退避した IP アドレスの形式が不正です。");
    }
    private static void ValidatePrefix(string prefix, int family)
    {
        if (prefix is null)
            throw new InvalidDataException("退避した prefix がありません。");
        var parts = prefix.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            || length < 0 || length > (family == 2 ? 32 : 128))
            throw new InvalidDataException("退避した prefix の形式が不正です。");
        ValidateIp(parts[0], family);
    }

    private static string NormalizeIp(string value)
    {
        var ip = IPAddress.Parse(value);
        return new IPAddress(ip.GetAddressBytes()).ToString();
    }
    private static string NormalizePrefix(string value)
    {
        var parts = value.Split('/');
        return NormalizeIp(parts[0]) + "/" + int.Parse(parts[1], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }
    private static string Key(params object[] values) => string.Concat(values.Select(value =>
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
    }));
    private static bool Equal(IEnumerable<string> expected, IEnumerable<string> actual) =>
        expected.Order(StringComparer.Ordinal).SequenceEqual(actual.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    private static IEnumerable<string> AdapterIdentities(IpStackSnapshot s) =>
        s.Adapters.Select(a => Key(a.AdapterId, a.Description, a.PnpDeviceId));
    private static IEnumerable<string> AdapterStates(IpStackSnapshot s) => s.Adapters.Select(a =>
        Key(a.AdapterId, a.Description, a.PnpDeviceId, a.HasNetworkConfiguration, a.NetbiosOptions,
            Key(a.StaticWinsServers.Select(NormalizeIp).Cast<object>().ToArray())));
    private static IEnumerable<string> InterfaceStates(IpStackSnapshot s, bool persistentOnly) =>
        s.Interfaces.Where(i => !persistentOnly || i.PolicyStore == "PersistentStore").Select(i =>
        {
            var automaticMetric = i.Settings.Any(value => value.Name == "AutomaticMetric" && value.Value is "Enabled" or "1");
            return Key(i.AdapterId, i.AddressFamily, i.PolicyStore,
                Key(i.Settings.Where(value => value.Name != "InterfaceMetric" || !automaticMetric)
                    .OrderBy(value => value.Name, StringComparer.Ordinal).Select(value => (object)Key(value.Name, value.Value, value.IsTimeSpan)).ToArray()));
        });
    private static IEnumerable<string> AddressStates(IpStackSnapshot s, bool persistentOnly) =>
        s.Addresses.Where(a => !persistentOnly || a.PolicyStore == "PersistentStore")
            .Select(a => Key(a.AdapterId, a.AddressFamily, a.PolicyStore, NormalizeIp(a.Address), a.PrefixLength, a.SkipAsSource, a.Type));
    private static IEnumerable<string> RouteStates(IpStackSnapshot s, bool persistentOnly) =>
        s.Routes.Where(r => !persistentOnly || r.PolicyStore == "PersistentStore")
            .Select(r => Key(r.AdapterId, r.AddressFamily, r.PolicyStore, NormalizePrefix(r.DestinationPrefix), NormalizeIp(r.NextHop), r.RouteMetric, r.Publish, r.Protocol));
    private static IEnumerable<string> DnsStates(IpStackSnapshot s) => s.Dns.Select(d =>
        Key(d.AdapterId, d.AddressFamily, d.Automatic, d.Automatic ? string.Empty : Key(d.ServerAddresses.Select(NormalizeIp).Cast<object>().ToArray()),
            d.AutomaticSuffix, d.AutomaticSuffix ? string.Empty : d.ConnectionSpecificSuffix.ToUpperInvariant(), d.RegisterThisConnectionsAddress, d.UseSuffixWhenRegistering));
}
