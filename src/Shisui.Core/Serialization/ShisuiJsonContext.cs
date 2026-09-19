using System.Text.Json.Serialization;
using Shisui.Core.Models;

namespace Shisui.Core.Serialization;

/// <summary>プラットフォーム非依存のモデル用 JSON シリアライズコンテキスト(NativeAOT/トリミング対応)。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(GamingNetworkProfileSnapshot))]
[JsonSerializable(typeof(GamingNetworkPropertySnapshot))]
[JsonSerializable(typeof(List<GamingNetworkProfileSnapshot>))]
internal sealed partial class ShisuiJsonContext : JsonSerializerContext;
