using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// 「おまかせ高速化設定」で TcpAckFrequency=1 を設定するかの判定。
/// 選択 NIC が現在の回線で、その回線の計測が 30 日以内かつ上り容量が 10 Mbps 未満のときだけ設定を省く
/// (上りの細い回線では ACK を即時に返すと上りを圧迫するため、Windows の既定に任せる)。
/// 回線・計測を確認できない場合は、従来どおり設定する。
/// </summary>
public static class UplinkAckPolicy
{
    /// <summary>これ未満を「上りが細い」とみなす NIC 送信容量 (bps)。</summary>
    public const long ThinUplinkBitsPerSecond = 10_000_000;

    /// <summary>計測を有効とみなす期間。</summary>
    public static readonly TimeSpan MaxMeasurementAge = TimeSpan.FromDays(30);

    /// <summary>
    /// 即時 ACK の設定を省くなら true。<paramref name="adapterConnectionName"/> は選択 NIC の接続名 (Windows の adapter.Id)。
    /// </summary>
    public static bool ShouldSkipImmediateAck(
        UplinkLimitState? state,
        string adapterConnectionName,
        IReadOnlyList<UplinkLimitProfile> profiles,
        DateTimeOffset nowUtc)
    {
        if (state is not { IsReadable: true, Line: { IsIdentified: true } line }
            || !line.AdapterName.Equals(adapterConnectionName, StringComparison.OrdinalIgnoreCase))
            return false;

        var profile = profiles.FirstOrDefault(p => line.Matches(p.AdapterGuid, p.GatewayMac));
        var age = nowUtc - profile?.MeasuredAtUtc;
        return profile is not null
            && age >= TimeSpan.Zero && age <= MaxMeasurementAge
            && profile.CapacityBitsPerSecond is > 0 and < ThinUplinkBitsPerSecond;
    }
}
