using System.Net.NetworkInformation;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="NetworkInterface.GetAllNetworkInterfaces"/> が返す一覧から、ユーザーが DNS を設定できる
/// 「本物のネットワークアダプタ」だけを選ぶ純粋関数 (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
///
/// GetAllNetworkInterfaces は ncpa.cpl (ネットワーク接続) に表示される実アダプタに加えて、
/// 各アダプタにぶら下がる NDIS フィルタ/バインディングの子インターフェイス
/// (例: "〜-QoS Packet Scheduler-0000" / "〜-WFP Native MAC Layer LightWeight Filter-0000") や、
/// Wi-Fi Direct 仮想アダプタ、未接続 (NotPresent) の疑似デバイスまで返す。これらは DNS 設定対象では
/// ないため除外する。
/// </summary>
public static class WindowsNetworkAdapterFilter
{
    /// <summary>
    /// 与えられた説明 (Description) が、同じ一覧内の別アダプタの Description を接頭辞に持つ
    /// フィルタ/バインディングの子インターフェイスかどうか。
    /// 子の Description は必ず "&lt;親の Description&gt;-&lt;フィルタ名&gt;-&lt;連番&gt;" の形をとるため、
    /// フィルタドライバ名をハードコードせずに (ウイルス対策・VPN 等が追加する未知の LWF にも効く形で) 判定できる。
    /// </summary>
    public static bool IsFilterBindingChild(string description, IReadOnlyCollection<string> allDescriptions) =>
        allDescriptions.Any(other =>
            !string.IsNullOrEmpty(other)
            && other.Length < description.Length
            && description.StartsWith(other + "-", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ユーザーが DNS を設定できる実アダプタなら true。ncpa.cpl に表示される非表示でないアダプタと一致する想定。
    /// </summary>
    public static bool IsUserConfigurable(
        string description,
        NetworkInterfaceType type,
        OperationalStatus status,
        IReadOnlyCollection<string> allDescriptions)
    {
        // 実際に DNS を持つのは有線 (Ethernet) と無線 (Wireless80211) のみ。
        // Loopback / Tunnel (Teredo / 6to4 / IP-HTTPS) はここで除外される。
        if (type is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
        {
            return false;
        }

        // 物理的に存在しない疑似デバイス (カーネルデバッガー等)。
        if (status == OperationalStatus.NotPresent)
        {
            return false;
        }

        // NDIS フィルタ/バインディングの子インターフェイス。
        if (IsFilterBindingChild(description, allDescriptions))
        {
            return false;
        }

        // Wi-Fi Direct 仮想アダプタ (ncpa.cpl でも非表示扱いの Microsoft 疑似アダプタ)。
        // Description はロケール非依存の英語ドライバ名なので固定文字列で判定してよい。
        if (description.Contains("Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
