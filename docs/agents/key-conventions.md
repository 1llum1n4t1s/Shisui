# 実装の規約

- **Async**: サービスのメソッドには `Async` 接尾辞を付け、`CancellationToken` を伝播する。
- **コマンドビルダーは純粋関数**: `*CommandBuilder` / `*Catalog` クラスの中で `Process` や OS 呼び出しをしない（単体テストできる状態を保つため）。OS へのアクセスはサービスとエグゼキューターに置き、プラットフォーム固有の入口には `[SupportedOSPlatform(...)]` を付ける。
- **Native AOT と JSON**: [build-test-commands.md](build-test-commands.md) の「Native AOT と JSON」にある、ソース生成とリリース経路の確認に従う。
- **設定の保存**: [DESIGN.md](../../DESIGN.md#設定と更新) に書かれた、読み込み失敗時の保存ブロックと NIC / Wi-Fi の復元スナップショットを維持する。保存の呼び出し元を変更するときは、保存失敗が完了済みの OS 変更とは別に報告されること、保存失敗時に更新設定がロールバックされることを確認する。
- **macOS 配布**: 出荷前に実機で確認する。実装とパーサーのテストだけでは互換性を示せない。現在対応している配布形態は DESIGN に書かれている。
