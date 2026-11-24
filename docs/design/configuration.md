# 低遅延を優先する設定方針

おまかせ高速化は、省電力・CPU 負荷・最大受信速度より、通信の待機と損失回復の遅れを減らすことを優先します。Windows 既定値であることだけを採用理由にしません。この方針は Windows の TCP/IP と対応 NIC に適用します。根拠の扱いは[根拠の扱い](conventions.md)、検証結果は[実機での検証結果](testing.md)を参照してください。

| 設定 | おまかせの選択 | 根拠と適用条件 |
| --- | --- | --- |
| ECN | 有効 | 対応する相手・経路で、損失に代えて輻輳通知を扱える。対応ルーターのキュー制御も必要で、単独で待ち行列を解消する機能ではない。[RFC 3168](https://www.rfc-editor.org/rfc/rfc3168.html) |
| timestamps | `allowed` から `enabled` | 送信接続でも交渉する。RTT 計測と PAWS を使えるようにするが、送信待ちを直接短くする設定ではない。[netsh](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface)、[RFC 7323](https://www.rfc-editor.org/rfc/rfc7323.html) |
| Fast Open / fallback | 明示的に有効 | 対応アプリ・サーバーでは、初期データ付き接続の開始を最大 1 RTT 節約できる。アプリと相手の対応が必要。fallback は互換性のため有効にする。[RFC 7413](https://www.rfc-editor.org/rfc/rfc7413.html) |
| 割り込み調整・EEE・省電力 | 対応する選択 NIC で無効 | 割り込みの待機や低電力状態からの復帰を減らす。CPU 負荷と消費電力の増加を許容する。詳細は[ゲーム向け NIC の詳細設定](section-04.md)と[電源管理](section-05.md)。[Microsoft](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-performance-tuning-nics) |
| RSS・LSOv2・チェックサム | 有効を維持 | Microsoft の低遅延向け指針も static offload の有効化を挙げる。LSO は大きな TCP 送信の分割であり、小さな UDP の待ちを直接取り除く設定ではない。 |
| TCP RSC・UDP URO | 無効を維持 | CPU 負荷より低遅延を優先する選択。TCP RSC の無効化は低遅延用途で利益がある場合の選択肢だが、合成のために TCP 通知を次の DPC まで遅らせる実装は仕様上禁止される。常にタイマー待ちが加わるとは説明しない。UDP URO によるゲーム遅延の改善量は未実測。[Microsoft](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-choose-nic#receive-segment-coalescing-rsc) |
| RACK/TLP・受信ウィンドウ | 組で有効・Normal を維持 | 損失検出と受信ウィンドウの不足を避ける。Experimental や再送タイマーの短縮が通常の RTT を短くするとは判断しない。[RFC 8985](https://www.rfc-editor.org/rfc/rfc8985.html) |
| 初回再送・SYN 再送 | `initialrto=1000 maxsynretransmissions=4` を 1 コマンドで組にし、Internet テンプレートの値を読み戻す | 接続要求が失われたときの再試行を早める。RTO 単独だと接続断念が 3000ms×2 回の 21 秒から 1000ms×2 回の 7 秒に早まるため、SYN 再送 4 回（1+2+4+8+16=31 秒）と必ず組にし、断念までの時間をどちらの OS 既定値（3000/2 または 1000/4。どちらかは未確認）以上にも保つ。実機（build 26300）で適用・読み戻し（1000/4）・元の値への復元を確認。通常の RTT を短くする設定ではなく、送信中の待ち行列にも効かない。手動の既定化は従来どおり 3000/2（未変更の Windows で既定値を確認できるまで現状維持）。遅延への効果は未測定 |

BBR2 は全 5 テンプレート、RACK/TLP は Compat を除く 4 テンプレートへ適用します。UDP は URO/USO だけを既定化し、UDP 全体の reset は行いません。TCP の既定化は不調な旧設定を除く修復工程とし、その後に RSS・ECN・timestamps・Fast Open / fallback の有効化と RSC の無効化を明示的に適用します。手動の既定化には、この上書きを含めません。

以下の値は強制しません。負荷・ドライバー・輻輳制御との組み合わせを確認する必要があり、低遅延に有利な共通値を確定できないためです。RX/TX バッファの一律最小化、RSS キュー数の最大化・固定 CPU 指定、USO の一律停止、HyStart・PRR の新たな強制値は追加しません。RTO は上の表の初回再送・SYN 再送の組だけを例外とし、それ以外の RTO 値は強制しません。

TCP 全体のペーシング（`pacingprofile=always`）と即時 ACK の扱いは[採用済みの設計判断](section-03.md)に記します。ペーシングは System32 の netsh の終了値と、`int tcp dump` の `set global` 行にある固定トークン `pacingprofile=always` の一意な読み戻しを確認し、保証するのは保存された構成であって各接続の実際のペーシング挙動や遅延ではありません（[OS コマンドと API の契約](api-reference.md)）。個別の既定化は引き続き `default` です。参照: [IETF BBR draft §4.2](https://www.ietf.org/archive/id/draft-ietf-ccwg-bbr-05.html)、[Microsoft netsh interface](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface)。

追加の参照: [TCP_FASTOPEN](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-tcp-socket-options)、[Intel Ethernet Adapters and Devices User Guide 30.5](https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/30.5/other-power-options/)、[Indicating Coalesced Segments](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/indicating-coalesced-segments)、[UDP RSC Offload](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/udp-rsc-offload)。

関連文書: [ネットワーク操作](commands.md)、[DESIGN.md](../../DESIGN.md)
