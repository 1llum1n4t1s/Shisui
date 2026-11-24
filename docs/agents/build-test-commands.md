# ビルド・テストのコマンドと検証

```bash
dotnet build Shisui.slnx
dotnet test Shisui.slnx --no-build
dotnet run --project src/Shisui.UI

# Run a single test class / method (MSTest via VSTest filter)
dotnet test Shisui.slnx --no-build --filter "FullyQualifiedName~WindowsDnsCommandBuilderTests"
dotnet test Shisui.slnx --no-build --filter "FullyQualifiedName~WindowsTcpStateParserTests.Parse_AllTemplatesBbr2_ReturnsEnabled"

# Local signed release (see "Auto-update & Release"). -SkipUpload = build + sign only, no cloud.
pwsh scripts/release-local.ps1 -SkipUpload
```

テストは `src/Shisui.Tests/AssemblyInfo.cs` の `Parallelize`（メソッド単位）で並列実行される。テストは static 状態・共有ファイル・環境変数に依存させない。

## ビルドとロックファイル

RID 固定の `packages.lock.json` は使っていないため、ローカルビルドで `--no-restore` を意識する必要はない。リリーススクリプト（`scripts/release-local.ps1`）は独自に RID 固定の self-contained publish を行い、ロックファイルには触れない。

## リリース成果物の出力先

- 既定の出力先は、ユーザー Temp 配下の一意な `Codex\Shisui\release-<id>` ディレクトリ。署名済みパッケージは `packages`、中間ビルド出力は `build` に置かれる。
- 明示的に選ぶときは `-WorkDirectory` に、ユーザー Temp 配下 `Codex\Shisui` 以下のパスを渡す。スクリプトは、リポジトリや Codex ホーム配下のパス、reparse point、`artifacts` という名前のパス要素を拒否する。
- 既存の作業ディレクトリを再利用するときは、許可された一時ルートであることを確認し、リンクを拒否してから標準のファイルシステム操作で清掃する。
- スクリプトは完了後もローカルの出力を残す。不要になったら、該当する清掃手順で確認して片付ける。
- `-UseEnvironmentalAotTools` は、すでに構成済みのネイティブツールチェーンを使うときだけ、publish に `IlcUseEnvironmentalTools=true` を渡す。

## Native AOT と JSON

Native AOT は `Directory.Build.props` でリポジトリ全体に有効化されている。シリアライズするアプリケーションモデルを追加するときは `ShisuiJsonContext` に登録する。リフレクションに依存する JSON 経路は、通常のビルドが通っても publish や実行時にだけ失敗することがある。実際の win-x64 AOT 配布経路は `pwsh scripts/release-local.ps1 -SkipUpload` で確認する。

## UI 検証

compiled bindings（`x:DataType`）でビルドし、管理者トークンで Windows アプリを起動して、変更した UI を操作し、stdout / stderr の Avalonia バインディングエラーを確認する。デバッグ時は、実行時の昇格でデバッガーが切り離されないよう、Visual Studio を管理者として起動する。スモークテストで起動するためだけに昇格ブロックを外さない。プロセスが生きているだけでは UI の動作を検証したことにならない。起動順序とマニフェストの根拠は [DESIGN.md](../../DESIGN.md#起動) にある。

## 起動と移行の制約

`asInvoker` を維持し、Velopack フックを移行・昇格・`SingleInstanceGuard` より先に処理し、製品の AppUserModelID を Debug ビルドでは無効のままにする。インストーラーの移行や清掃を変更するときは、[DESIGN.md](../../DESIGN.md#インストール移行の信頼境界) に書かれた署名・ハンドル・パスの検査と、清掃対象の範囲の限定を維持する。
