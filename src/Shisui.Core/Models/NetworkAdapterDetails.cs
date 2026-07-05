namespace Shisui.Core.Models;

/// <summary>
/// ネットワークアダプタの読み取り専用詳細情報 (リンク速度・MAC アドレス等)。
/// <see cref="NetworkAdapterInfo"/> と別モデルにしてあるのは、アダプタ一覧の取得
/// (頻繁に呼ばれる) を軽量に保ち、詳細情報は選択中のアダプタ 1 件だけを都度取得するため。
/// </summary>
public sealed record NetworkAdapterDetails(
    string Id,
    string? MacAddress,
    string? LinkSpeedText,
    string? MediaType,
    bool IsUp);
