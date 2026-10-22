# 変更履歴

Git のバージョン記録・コミット差分と既存の変更履歴をもとに、確認できた版ごとの変更点をまとめています。「Git 記録日」は公開日ではありません。番号の欠番だけから未確認のリリースは補っていません。

## 未リリース

## [1.0.27] — 2026-10-23

- おまかせ高速化設定に、全NICの接続設定を退避してからIPスタックを初期化・復元する処理を追加した。固定IP・経路・DNSと自動取得方法を保持し、中断時の復旧と再起動後の確認に対応した
- IPsecの接続状態削除と、WINS利用環境でのNetBIOS名の解放・再登録をおまかせ高速化設定へ追加した
- IP設定の復旧が未完了のときは案内を表示し、退避した設定へ戻せるようにした

## [1.0.26] — Git 記録日: 2026-10-13

- おまかせ高速化設定に、対応するRealtek PCIe有線LANのFlow Control・Advanced EEE無効化と、対応NICのUDP受信合体（URO）無効化を追加した
- 「自動最適化」を起動時に表示し、ゲーム向けNIC設定の個別ボタンを統合、設定説明を項目ごとの短い一覧にした
- 処理中は画面全体をロックして進捗を表示し、Ping測定は進捗表示から中止できるようにした
- 設定ファイルの読取失敗後に、既定値の保存で元の設定と復元情報が失われる問題を修正した
- 設定保存の失敗を表示し、更新設定は変更を戻すようにした
- DNS情報の取得失敗を「なし」と区別して表示し、一覧更新時の表示エラーを修正した
- ネットワーク診断の修復対象を選択中のNICに限定し、別NICの警告だけで操作できる状態を解消した

出典: [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/release/1.0.25...release/1.0.26)。

## [1.0.25] — Git 記録日: 2026-10-12

- おまかせ高速化設定を低遅延重視にし、RSS 有効・RSC 無効とゲーム向け NIC 設定をまとめて適用できるようにした
- DHCP の IP アドレス再取得、カスタム LSP がない場合の Winsock リセット、WinHTTP キャッシュ消去、経路表の再構築準備をおまかせ処理へ追加した
- Wi-Fi の省電力を AC 接続時だけ最大パフォーマンスにし、元の設定の保存・復元に対応した
- 各画面の設定説明を簡潔にし、起動時の不要な設定保存と古い TCP 取得結果による表示の上書きを解消した
- 中止済みコマンドの起動を防止し、電源設定の復旧処理が待ち続けないようにした

出典: [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/1f22e9e29112c4fb609c05d27c281f8e6b1e7784...a6d0779b25cbd4587ce6527fc96af2230411fce0)。

## [1.0.24] — Git 記録日: 2026-09-22

- Realtek製の物理Ethernet NICで、対応しているグリーンイーサネット・Gigabit Lite・Power Saving Modeをゲーム向け低遅延設定の対象に追加

出典: [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/release/1.0.23...release/1.0.24)。

## [1.0.23] — Git 記録日: 2026-09-21

- おまかせ高速化設定とメンテナンス画面から、現在の電源プランのPCI Express省電力をAC接続時だけ無効化できるようにした
- ワイルドカードを含むネットワークアダプター名でも対象を完全一致で選び、別のアダプターを誤操作しないようにした
- 旧版からの移行・配置修復で、署名検証済みMSIの実体パスを使い続けるよう安全性を強化した

出典: [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/release/1.0.22...release/1.0.23)。

## [1.0.22] — Git 記録日: 2026-09-20

- 有線・Wi-Fiのゲーム向け低遅延設定を追加し、変更前の値を保存して安全に復元可能にした
- Pingを4・30・100回から選べるようにし、損失率・p95・ジッターを含む遅延統計と中止操作を追加
- 外部コマンドの開始・終了時刻、所要時間、終了コード、標準出力・標準エラーを詳細ログへ記録
- アダプター名を含むPowerShell実行とMTU復元中の選択競合を堅牢化し、RACK/TLPの不要な再設定を抑止

出典: [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/release/1.0.21...release/1.0.22)。

## [1.0.21] — Git 記録日: 2026-09-20

- おまかせ高速化設定で BBR2・RACK/TLP を有効化し、UDP オフロード（URO/USO）を Windows 既定へ復元
- BBR2 と受信ウィンドウの実効値を適用後に読み戻し、ポリシー上書きや不完全な状態取得を検出
- Windows のシステムコマンド解決と MSI 移行時の署名検証を強化

出典: [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/release/1.0.20...release/1.0.21)。

## [1.0.20] — Git 記録日: 2026-08-30

- 依存関係と配布基盤を更新
- 計測機能の削除とネットワーク処理の堅牢化
- NuGet依存関係を更新 (#2)
- VelopackUpdateDialog.Avaloniaを1.0.11から1.0.14へ更新 (patch)
- Avaloniaファミリー4パッケージを12.1.0から12.1.1へ更新 (patch)
- MSTestを4.3.2から4.3.3へ更新 (patch)

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/2c1c154c36830667dc5498fdfd87467f475f11f6) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/6499bf2198e412bfe244a83bb106f26965c703e5...2c1c154c36830667dc5498fdfd87467f475f11f6)。

## [1.0.19] — Git 記録日: 2026-07-22

- MSI移行の署名検証強化と例外処理を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/6499bf2198e412bfe244a83bb106f26965c703e5) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/82d9b485cb155731376261a282fc63a2a2585dcf...6499bf2198e412bfe244a83bb106f26965c703e5)。

## [1.0.18] — Git 記録日: 2026-07-22

- 切断済みデバイス整理をおまかせ設定へ統合

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/82d9b485cb155731376261a282fc63a2a2585dcf) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/43145ad524e7a3ec24b4744f6c672c1ae5bdec6a...82d9b485cb155731376261a282fc63a2a2585dcf)。

## [1.0.17] — Git 記録日: 2026-07-21

- MTU復元と接続名整理を追加

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/43145ad524e7a3ec24b4744f6c672c1ae5bdec6a) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/81d2ca05c4e395b91b1fd39a97816249b9093b4c...43145ad524e7a3ec24b4744f6c672c1ae5bdec6a)。

## [1.0.16] — Git 記録日: 2026-07-21

- MSI配置修復の引数を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/81d2ca05c4e395b91b1fd39a97816249b9093b4c) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/03b5dfde76b802022fddd9fd934f29031ee7b3b7...81d2ca05c4e395b91b1fd39a97816249b9093b4c)。

## [1.0.15] — Git 記録日: 2026-07-21

- MSI移行と残骸回収を修正
- ランディングページのMSIリンクを修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/03b5dfde76b802022fddd9fd934f29031ee7b3b7) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/444cc47936b8992bca73cef62d9d84ae0dd5b259...03b5dfde76b802022fddd9fd934f29031ee7b3b7)。

## [1.0.14] — Git 記録日: 2026-07-21

- 使い込んだPC向け診断の状態取得を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/444cc47936b8992bca73cef62d9d84ae0dd5b259) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/31bca72ecb800a1a77804ab45e1d1257ba7e1090...444cc47936b8992bca73cef62d9d84ae0dd5b259)。

## [1.0.13] — Git 記録日: 2026-07-21

- 自動最適化とネットワーク診断を改善

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/31bca72ecb800a1a77804ab45e1d1257ba7e1090) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/66393b18fa92abbbb865b7225ee4d7b274e4630c...31bca72ecb800a1a77804ab45e1d1257ba7e1090)。

## [1.0.12] — Git 記録日: 2026-07-21

- 自動最適化を統合・拡張
- MSIサイズ検証の単一値処理を安定化
- MSI配信サイズ検証のヘッダー処理を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/66393b18fa92abbbb865b7225ee4d7b274e4630c) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/5853e11ac6c867bfe8e1e24944ca6bc2c5813eec...66393b18fa92abbbb865b7225ee4d7b274e4630c)。

## [1.0.11] — Git 記録日: 2026-07-21

- 計測とインストール移行の安全性を改善

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/5853e11ac6c867bfe8e1e24944ca6bc2c5813eec) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/0bebbbfd6e88448cfcf0acbcce34a6a91d18e02a...5853e11ac6c867bfe8e1e24944ca6bc2c5813eec)。

## [1.0.10] — Git 記録日: 2026-07-21

- TCP既定化と低遅延計測を強化

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/0bebbbfd6e88448cfcf0acbcce34a6a91d18e02a) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/8e3b132da1a75cab7064daff9b62b128e1233505...0bebbbfd6e88448cfcf0acbcce34a6a91d18e02a)。

## [1.0.9] — Git 記録日: 2026-07-18

- 旧スタートメニューショートカットを移行

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/8e3b132da1a75cab7064daff9b62b128e1233505) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/fef4c6d14b5bc554d721bee067d45341a5613878...8e3b132da1a75cab7064daff9b62b128e1233505)。

## [1.0.8] — Git 記録日: 2026-07-18

- スタートメニュー直下へのショートカット登録

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/fef4c6d14b5bc554d721bee067d45341a5613878) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/14996921f7c1da9cc3b5ebcc6c1c2af292233cc1...fef4c6d14b5bc554d721bee067d45341a5613878)。

## [1.0.7] — Git 記録日: 2026-07-10

- デバッガアタッチ時に自己昇格をスキップしてデバッグ実行を継続可能に

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/14996921f7c1da9cc3b5ebcc6c1c2af292233cc1) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/d972c92f0a504dc3cff7c61f2bdc420d5c7cab21...14996921f7c1da9cc3b5ebcc6c1c2af292233cc1)。

## [1.0.6] — Git 記録日: 2026-07-08

- WAN Miniport 仮想デバイスをアダプタ一覧から除外

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/d972c92f0a504dc3cff7c61f2bdc420d5c7cab21) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/e4647c8f8131be082062c4a387e1ce8fb9cb4f2e...d972c92f0a504dc3cff7c61f2bdc420d5c7cab21)。

## [1.0.5] — Git 記録日: 2026-07-08

- おまかせ高速化設定のキャッシュクリア強化と実行ログの高さ調節

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/e4647c8f8131be082062c4a387e1ce8fb9cb4f2e) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/c6d4c648ca271a3e26d70d3402d354808dec105d...e4647c8f8131be082062c4a387e1ce8fb9cb4f2e)。

## [1.0.4] — Git 記録日: 2026-07-06

- セキュリティ・並行性バグの修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/c6d4c648ca271a3e26d70d3402d354808dec105d) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/ebbf99da00c10e801429dbe908116ff3d1a7f0a4...c6d4c648ca271a3e26d70d3402d354808dec105d)。

## [1.0.3] — Git 記録日: 2026-07-06

- DoT・診断タブ・auto-tuningベンチマーク等を追加

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/ebbf99da00c10e801429dbe908116ff3d1a7f0a4) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/efa9fe351b86580dcf37d798551cf52035ba53a4...ebbf99da00c10e801429dbe908116ff3d1a7f0a4)。

## [1.0.2] — Git 記録日: 2026-07-05

- DNS over HTTPS トグルとアップデーターUIを刷新

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/efa9fe351b86580dcf37d798551cf52035ba53a4) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/a931484db9cb85cf68113aecfed2fd1eb6a159f6...efa9fe351b86580dcf37d798551cf52035ba53a4)。

## [1.0.1] — Git 記録日: 2026-07-05

- Velopack インストーラーの管理者権限昇格エラーを修正

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/a931484db9cb85cf68113aecfed2fd1eb6a159f6) / [変更差分](https://github.com/1llum1n4t1s/Shisui/compare/b87792f93632acb4b9fc933bbdf9a98dfa09d323...a931484db9cb85cf68113aecfed2fd1eb6a159f6)。

## [1.0.0] — Git 記録日: 2026-07-03

- 止水（Shisui）初回リリース

出典: [版の記録](https://github.com/1llum1n4t1s/Shisui/commit/b87792f93632acb4b9fc933bbdf9a98dfa09d323)。
