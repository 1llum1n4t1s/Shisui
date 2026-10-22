# AGENTS.md

This file provides guidance to coding agents working in this repository.

## Project Overview

Shisui is a Windows/macOS network configuration app. User-facing features and the supported distribution
are documented in [README.md](README.md); structure and design boundaries are documented in [DESIGN.md](DESIGN.md).

**Language**: Japanese for UI, comments, commit messages, and README. This AGENTS.md is in English.

## Build & Test Commands

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

No RID-locked `packages.lock.json` is used, so there is no `--no-restore` dance for local builds. The release
script (`scripts/release-local.ps1`) does its own RID-locked self-contained publish; it does not touch a lockfile.
Release output defaults to a unique user-Temp `Codex\Shisui\release-<id>` directory, with signed packages
under `packages` and intermediate build output under `build`. To select it explicitly, use `-WorkDirectory`
with a path below user-Temp `Codex\Shisui`; the script rejects repository/Codex-home paths, reparse points,
and path components named `artifacts`. Reusing an existing work directory checks the allowed temporary root and rejects links before standard filesystem cleanup. The script retains local output after completion; inspect and
clean it through the applicable cleanup procedure once it is no longer needed. `-UseEnvironmentalAotTools`
passes `IlcUseEnvironmentalTools=true` to publish only when using an already configured native toolchain.
Native AOT is enabled repository-wide through `Directory.Build.props`. When adding serialized application models,
register them in `ShisuiJsonContext`; reflection-based JSON paths can fail only at publish/runtime even when a normal
build succeeds. Use `pwsh scripts/release-local.ps1 -SkipUpload` to verify the actual win-x64 AOT distribution path.

**UI verification**: build with compiled bindings (`x:DataType`), launch the Windows app with an
administrator token, exercise the changed UI, and inspect stdout/stderr for Avalonia binding errors.
For debugging, start Visual Studio as administrator so runtime elevation does not detach the debugger.
Do not remove the elevation block merely to launch a smoke test. A process that stays alive alone does
not verify UI behavior. Startup ordering and the manifest rationale are documented in [DESIGN.md](DESIGN.md#起動).

**Startup and migration constraints**: preserve `asInvoker`, process Velopack hooks before migration,
elevation, and `SingleInstanceGuard`, and keep the product AppUserModelID disabled in Debug builds.
When modifying installer migration or cleanup, preserve the signature/handle/path checks and bounded
cleanup targets described in [DESIGN.md](DESIGN.md#インストール移行の信頼境界).

## Architecture

現在の構造・責務・境界・不変条件の正本は [DESIGN.md](DESIGN.md)。実装領域ごとの詳細は
[references/architecture.md](references/architecture.md)。**下の領域を触る前に必ず該当節を読む**。

| 触る対象 | 読む節 |
| --- | --- |
| プロジェクト構成、どこに何があるか | Project Structure |
| 外部コマンド実行（PowerShell / netsh） | Command execution model — string arguments, not `ArgumentList` |
| DI 登録、Windows 専用機能の扱い | DI: Windows-only features are optional dependencies |
| DNS プリセット、DoH / DoT | DNS Preset Catalog / DoH toggle / DoT toggle |
| ワンクリック最適化 | One-click optimization |
| ゲーム向け NIC 設定 | Gaming NIC profile |
| 現在値の読み取り（ロケール非依存） | Reading current state is locale-independent |
| ネットワーク診断 | Used-PC network diagnostics / Network diagnostics |
| アダプタ一覧・詳細・ゴースト削除 | Adapter list filtering / Adapter details / Disconnected network device cleanup |
| 画面、テーマ、コントロール | UI Framework |
| 設定の保存、ログ | Settings & Logging |

## Key Conventions

- **Async**: service methods use `Async` suffix, propagate `CancellationToken`.
- **Command builders are pure functions**: no `Process`/OS calls inside `*CommandBuilder` / `*Catalog` classes —
  keeps them unit-testable. OS access belongs to services and executors; annotate platform-specific entry points with `[SupportedOSPlatform(...)]`.
- **Native AOT and JSON**: follow the source-generation and release-path checks in "Build & Test Commands".
- **Settings persistence**: preserve the load-failure save block and recovery journals described in
  [DESIGN.md](DESIGN.md#設定と更新). When changing save callers, verify that save failures are reported
  separately from completed OS changes and that update preferences roll back on save failure.
- **macOS distribution**: verify on real hardware before shipping; implementation and parser tests alone
  do not establish compatibility. The currently supported distribution is documented in DESIGN.

## Auto-update & Release

The distribution and update design is documented in [DESIGN.md](DESIGN.md#設定と更新).
The authoritative dependency versions are in `src/Shisui.UI/Shisui.UI.csproj`; release CLI pins are in
`scripts/release-local.ps1`. Consult these files rather than version numbers in descriptive references.

- Keep the direct `Velopack` package and `$VpkVersion` CLI pin in sync; neither is resolved-latest.
  The dialog package can be updated independently. Verify all update/package changes through
  `pwsh scripts/release-local.ps1 -SkipUpload`, including win-x64 Native AOT, MSI Directory normalization,
  re-signing, and signature checks. Preserve the explicit `vpk pack --runtime $runtime` matching publish.
- Signing requires SimplySign Desktop to be authenticated with a valid certificate
  (`CN=Open Source Developer Yuichiro Shinozaki`) and Windows SDK x64 `signtool.exe`.
  `-SkipUpload` builds and signs without cloud writes. Upload additionally requires
  `cloudflare.api_token` from the release script's configured secret source.
- `vava.config.json` runs certificate precheck and the local release script through `/vava`.
  Change versions only through `/vava`; do not hand-edit `Directory.Build.props` `<Version>`.
- Publish signed PerMachine MSI, never the generated PerUser Setup.exe. Preserve publication ordering,
  rollback, propagation checks, and retention documented in DESIGN.
- Keep `Shisui.UI.exe` and its assembly name: changing it breaks `avares://Shisui.UI/...` resources.
  The shortcut/display name is `Shisui` via `--packTitle`.
- Preserve the legacy `shisui.nephilim.jp` update endpoint until 2027/05/31. Redirect only `/`;
  update manifests and packages must remain directly downloadable during migration.
