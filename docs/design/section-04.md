# ゲーム向け NIC の詳細設定

Windows 専用で、おまかせ高速化設定の一括適用に含めます。画面に個別の適用欄は設けません。電源管理の項目は [電源管理](section-05.md) に分けます。

- **共通項目**: 物理 Ethernet/Wi-Fi の対応する標準プロパティで、RSS・LSOv2 IPv4/IPv6 を 1、IP IPv4・TCP/UDP IPv4/IPv6 のチェックサムオフロードを 3（送受信有効）にします。二値項目は 0/1 の両方、チェックサム項目は元値と目標値がドライバーの対応値に含まれることを確認します。CPU 負荷への効果と、ゲーム・WAN の遅延改善は区別します。
- **UDP RSC・割り込み調整**: 標準 `*UdpRsc` を公開し 0/1 に対応する物理 Ethernet/Wi-Fi では URO を 0 にします。OS 側の UDP URO/USO 既定化とは別に扱います。物理 Ethernet の `*InterruptModeration` / `*EEE`、物理 Wi-Fi の `*InterruptModeration` も対応値を確認して無効化します。
- **Realtek（有線）**: `*FlowControl` と `AdvancedEEE` は、PCI `VEN_10EC` を確認できる物理 Ethernet だけ 0 にします。USB やプロバイダー名だけの判定では変更しません。Flow Control は標準 0/1/2/3 のうち元値と 0 の公開対応、Advanced EEE は 0/1 の公開対応を必須にします。RTL8125BG の導入済み INF で Flow Control の公開値 0/3、Advanced EEE の 0/1 を確認しています。
- **MediaTek（無線）**: 定義を確認済みの `LowPowerEnable` / `UAPSDSupport` を扱います。A-MSDU 送信（`AMSDUTx`）は、物理 Wi-Fi（型付き bool true・整数 71）、provider 完全一致 `MediaTek, Inc.`、PCI `VEN_14C3&DEV_0616` を区切り境界まで照合した RZ616 に限定して OFF（0）にします。公開列挙値 0/1 の両方が必要です。既存の変更前値保存・競合検知・復元・保存値読み戻しを使い、未公開項目やキーを作成しません。送信側の集約を減らす機構を優先し、一貫した遅延改善は実証していません（[Microsoft WDI transmit operations](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wdi-transmit-operations-and-offloads)）。A-MSDU 受信（`AMSDURx`）は、確認した RZ616 ドライバーで OFF が集約待ち削減になる機構を確認できないため現値を維持し、書換え・既定化を行いません。送信側と受信側を同じ理由で無効化しません（[Microsoft WDI receive operations](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wdi-receive-operations-and-offloads)）。
- **許可リストと保存**: 媒体・ベンダーごとの許可リストを query・parser・書込み・復元で照合します。EEE を Wi-Fi へ、他社の独自項目を他社へ流用しません。変更前の値を AppSettings へ保存してから書き込み、設定値を再取得します。GUID と説明で対象を照合し、再適用で元の値を上書きせず、外部変更との競合も上書きしません。
- **対象外と再起動**: 仮想 NIC、未知のプロパティ値、許可対象外のオフロード・リンク速度・バッファー・周波数帯・チャンネル幅・ローミングは変更しません。一括処理の TCP グローバル設定とは責務を分けます。詳細プロパティと Selective Suspend の変更には `-NoRestart` を指定し、WDF/WMI 経路でも NIC の再起動を要求せず、適用・復元後は PC の再起動を要します。ドライバー設定として保存された値の読み戻しと、稼働中の反映・性能の確認は区別します。
- **共有ゲートと失敗**: 単独の適用・復元は共有ゲートを取得します。一括適用は呼出し元の共有リースを保持したまま `ApplyWithinMutationLeaseAsync` を呼び、接続名整理より前に実行します。NIC 設定の失敗・空結果・例外は全体の失敗結果へ含めます。

関連文書: [低遅延を優先する設定方針](configuration.md)、[DESIGN.md](../../DESIGN.md)
