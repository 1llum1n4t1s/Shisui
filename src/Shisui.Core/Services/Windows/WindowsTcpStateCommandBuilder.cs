namespace Shisui.Core.Services.Windows;

/// <summary>
/// 現在の TCP / BBR2 設定を「ロケール非依存で」読み取るための PowerShell コマンド。
///
/// netsh の `show global` / `show supplemental` はラベル (左側) が OS 表示言語で翻訳されるため、
/// テキストをパースすると英語以外の Windows で壊れる (Microsoft 公式ドキュメントも既定テキスト出力の
/// スクリプト処理を非推奨としている)。そこで NetTCPIP モジュールのコマンドレットを使い、
/// 英語固定の列挙値 (Enabled / Disabled / BBR2 等) を KEY=VALUE 形式で出力させて読み取る。
/// FastOpen だけはプロパティが無いため、netsh dump の再実行用コマンドの固定キーワードから読む。
/// KEY はこちらで英語固定にするので、出力全体がロケール非依存になる。
/// </summary>
public static class WindowsTcpStateCommandBuilder
{
    public const string FileName = "powershell";

    // -NoProfile: ユーザープロファイルを読み込まず高速・副作用なし。-NonInteractive: プロンプトを出さない。
    // FastOpen の補助取得失敗は空値に限定し、他の cmdlet の失敗は従来どおり伝播する。
    public const string Arguments =
        "-NoProfile -NonInteractive -Command \"" +
        "$ErrorActionPreference='Stop';" +
        "$o=Get-NetOffloadGlobalSetting;" +
        "$t=Get-NetTCPSetting -SettingName Internet;" +
        "'RSS='+$o.ReceiveSideScaling;" +
        "'RSC='+$o.ReceiveSegmentCoalescing;" +
        "'ECN='+$t.EcnCapability;" +
        "'TIMESTAMPS='+$t.Timestamps;" +
        "$fastOpen='';" +
        "try{" +
        "$dump=@(& (Join-Path ([Environment]::SystemDirectory) 'netsh.exe') interface tcp dump 2>$null);" +
        "if($LASTEXITCODE -eq 0){" +
        "$globalLines=@($dump|Where-Object{$_ -match '^\\s*set\\s+global(?:\\s|$)'});" +
        "if($globalLines.Count -eq 1){" +
        "$tokens=[regex]::Matches($globalLines[0],'(?i)(?<!\\S)fastopen=(\\S*)');" +
        "if($tokens.Count -eq 1){" +
        "switch($tokens[0].Groups[1].Value){'enabled'{$fastOpen='Enabled'}'disabled'{$fastOpen='Disabled'}}" +
        "}}}" +
        "}catch{$fastOpen=''};" +
        "'FASTOPEN='+$fastOpen;" +
        "'AUTOTUNE='+$t.AutoTuningLevelLocal;" +
        "'AUTOTUNE_POLICY='+$t.AutoTuningLevelGroupPolicy;" +
        "'AUTOTUNE_SOURCE='+$t.AutoTuningLevelEffective;" +
        "Get-NetTCPSetting -SettingName Internet,InternetCustom,Datacenter,DatacenterCustom,Compat|%{'CC='+$_.SettingName+'|'+$_.CongestionProvider}" +
        "\"";
}
