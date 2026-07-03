namespace Shisui.Core.Models;

/// <summary>
/// 現在は接続されていない (切断済み) ネットワーククラスのデバイス登録。
/// 取り外した USB アダプタや、アンインストールした VPN クライアントの仮想アダプタ等のレジストリ上の
/// 残骸が典型例。ただし Windows 標準の WAN Miniport (VPN 機能で使う仮想デバイス) も同じ条件で
/// 列挙されるため、<see cref="IsLikelyMicrosoftVirtualDevice"/> で区別できるようにしている
/// (削除の可否を自動判定するものではなく、UI 側で警告表示するための材料)。
/// </summary>
public sealed record GhostAdapterInfo(
    string InstanceId,
    string Description,
    string Manufacturer,
    string DriverName,
    bool IsLikelyMicrosoftVirtualDevice);
