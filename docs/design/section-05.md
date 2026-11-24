# 電源管理（NIC・Wi-Fi・PCI Express）

おまかせ高速化設定が変更する電源項目です。取得できない値や対象外の値は変更しません。

- **Wi-Fi 電源プラン**: 現在のプランの AC 値だけを最大パフォーマンスへ変更し、DC 値を読み戻して保持を確認します。変更前の AC 値は電源プラン GUID ごとに AppSettings の `WifiPowerPlanSnapshots` へ保存し、再適用では上書きせず、メンテナンスから復元します。復元時に現在値が適用値・元値のどちらでもない場合は外部変更として保護します。未確認の設定や変更中のプラン切替は成功扱いしません。
- **PCI Express**: 現在の電源プランのリンク状態の電源管理だけを対象に、AC 値をオフにします。変更前の取得に失敗した場合は書き込まず、適用後は AC がオフ・DC が変更前と同じことを確認します。プランの切替や DC 値の書込みは行いません。メンテナンス画面からも個別に実行できます。選択 NIC 以外の機器にも影響し、消費電力・発熱が増える可能性を画面に表示します。
- **Realtek の省電力（有線）**: プロバイダー名または PCI/USB ベンダー ID で Realtek と確認できる物理 Ethernet で、プロパティが存在して 0/1 に対応する場合だけ `EnableGreenEthernet` / `GigaLite` / `PowerSavingMode` を扱います。RTL8168H の INF で定義を確認しています。
- **デバイスの電源オフ（Realtek）**: WDF キーがあれば、実機で確認した `rt68cx21x64.sys` の既存 `IdleInWorkingState` DWORD 0/1 に限り 0 を保存します。キーや値の作成・削除、既定値の推測、未確認ドライバーへの書込みはしません。WDF キーがない場合だけ、電源管理タブの `MSPower_DeviceEnable` を試みます。対象の PnP ID と数字のインスタンス接尾辞を完全照合し、Active と Enable が bool かつ Active=true の単一インスタンスだけを扱います。WDF の確認に失敗しても WMI へは切り替えず、理由を報告します。どちらも元値とドライバーファイル名の保存、競合検出、復元、保存値の読み戻しを行い、方式やドライバーが変わった復元情報は拒否します。書込み直前にもキーの有無とドライバーファイル名を再照合し、方式や対象の変化による誤書込みを防ぎます。`PnPCapabilities` は変更しません。WDF への書込みは保存値の確認で、稼働中の反映は未確認です。
- **Selective Suspend**: 詳細プロパティやレジストリの有無から推測せず、電源管理 API で読み書きします。親設定 `AllowComputerToTurnOffDevice` が Enabled のときだけ、Selective Suspend の Enabled / Disabled を元値として扱います。親設定がそれ以外または取得不能なら他の電源管理値は未定義のため対象外です。デバイスの電源オフ設定と Selective Suspend は同時に変更せず、前者を優先します。既存の Selective Suspend 復元情報がある場合は、新しい電源オフ設定の変更を拒否します。方式の変更を検出したら誤復元を避け、復元情報を保持して報告します。各経路の表示値を同一視せず、非対応はスキップし、取得エラーは別途報告します。

参照: [WDF の利用者制御](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/user-control-of-device-idle-and-wake-behavior)、[電源管理タブ仕様](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/wmi-and-the-power-management-tab)、[電源管理設定仕様](https://learn.microsoft.com/en-us/windows/win32/fwp/wmi/netadaptercimprov/msft-netadapterpowermanagementsettingdata)

関連文書: [ネットワーク操作](commands.md)、[DESIGN.md](../../DESIGN.md)
