namespace Shisui.Core.Services.Windows;

/// <summary>IProgress の中継で同期コンテキストへの二重ポストを発生させないための内部ラッパー。</summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
