namespace Shisui.Core.Models;

/// <summary>選択 NIC の TcpAckFrequency 明示値。既定は値なし、取得不能は Unknown。</summary>
public sealed record TcpAckFrequencyState(bool IsKnown, int? Value)
{
    public static TcpAckFrequencyState Unknown { get; } = new(false, null);
    public static TcpAckFrequencyState Default { get; } = new(true, null);
}
