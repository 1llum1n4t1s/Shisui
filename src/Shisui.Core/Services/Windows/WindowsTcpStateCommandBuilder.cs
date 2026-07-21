namespace Shisui.Core.Services.Windows;

/// <summary>
/// 現在の TCP / BBR2 設定を「ロケール非依存で」読み取るための PowerShell コマンド。
///
/// netsh の `show global` / `show supplemental` はラベル (左側) が OS 表示言語で翻訳されるため、
/// テキストをパースすると英語以外の Windows で壊れる (Microsoft 公式ドキュメントも既定テキスト出力の
/// スクリプト処理を非推奨としている)。そこで NetTCPIP モジュールのコマンドレットを使い、
/// 英語固定の列挙値 (Enabled / Disabled / BBR2 等) を KEY=VALUE 形式で出力させて読み取る。
/// KEY はこちらで英語固定にするので、出力全体がロケール非依存になる。
/// </summary>
public static class WindowsTcpStateCommandBuilder
{
    public const string FileName = "powershell";

    // -NoProfile: ユーザープロファイルを読み込まず高速・副作用なし。-NonInteractive: プロンプトを出さない。
    // FastOpen は Get-NetTCPSetting に公開プロパティが無く空になる (取得非対応)。
    public const string Arguments =
        "-NoProfile -NonInteractive -Command \"" +
        "$o=Get-NetOffloadGlobalSetting;" +
        "$t=Get-NetTCPSetting -SettingName Internet;" +
        "'RSS='+$o.ReceiveSideScaling;" +
        "'RSC='+$o.ReceiveSegmentCoalescing;" +
        "'ECN='+$t.EcnCapability;" +
        "'TIMESTAMPS='+$t.Timestamps;" +
        "'FASTOPEN='+$t.FastOpen;" +
        "'AUTOTUNE='+$t.AutoTuningLevelLocal;" +
        "Get-NetTCPSetting -SettingName Internet,InternetCustom,Datacenter,DatacenterCustom,Compat|%{'CC='+$_.SettingName+'|'+$_.CongestionProvider}" +
        "\"";
}
