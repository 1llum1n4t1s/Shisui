# 根拠の扱い

低遅延の設定について、次の区別を守ります。

- 設定が受理されたこと、保存値・実効値の確認、通信相手との機能交渉、遅延の比較測定は別々に扱い、どれか一つで別の確認を代用しません。
- この方針は Windows の TCP/IP と対応 NIC に適用します。すべてのゲーム・経路で改善するという意味にはしません。
- 資料は 2026 年 10 月 23 日に確認しました。Microsoft の NIC 指針は Windows Server 向けのため、Windows 11 の各ゲームでの効果を示す資料とは区別します。
- ローカルの Windows ビルド 26300 で設定値と照会経路を確認していますが、このプロファイルの RTT・ジッター・損失・フレーム時間の比較測定は未実施です。
- BBR2 と loopback Large MTU の既存の組み合わせは維持しますが、全通信や UDP ゲームの低遅延を示す測定はありません。
- 製品の媒体制限ではなく、実測で改善が証明された設定や、すべての環境で低遅延になる設定とは説明しません。

## オフロードの根拠

- LSO は大きな TCP 送信の分割であり、小さな UDP の待ちを直接取り除く設定ではありません。Microsoft の低遅延向け指針は static offload の有効化も挙げています。
- TCP RSC の無効化は[低遅延用途で利益がある場合の選択肢](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-choose-nic#receive-segment-coalescing-rsc)です。ただし、合成のために TCP 通知を次の DPC まで遅らせる実装は仕様上禁止されるため、常にタイマー待ちが加わるとは説明しません（[Indicating Coalesced Segments](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/indicating-coalesced-segments)）。
- UDP URO によるゲーム遅延の改善量は未実測です（[UDP RSC Offload](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/udp-rsc-offload)）。

## その他の参照

- ペーシング: [IETF BBR draft §4.2](https://www.ietf.org/archive/id/draft-ietf-ccwg-bbr-05.html)、[Microsoft netsh interface](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface)
- Fast Open: [TCP_FASTOPEN](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-tcp-socket-options)
- NIC の省電力: [Intel Ethernet Adapters and Devices User Guide 30.5](https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/30.5/other-power-options/)

関連文書: [実機での検証結果](testing.md)、[DESIGN.md](../../DESIGN.md)
