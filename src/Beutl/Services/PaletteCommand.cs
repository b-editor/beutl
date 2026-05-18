using Avalonia.Input;

namespace Beutl.Services;

public sealed record PaletteCommand(
    string Id,
    string DisplayName,
    string? Description,
    string CategoryName,
    KeyGesture? KeyGesture,
    Func<bool> CanExecute,
    Action Execute
)
{
    // ハンドラーが状態変化を通知できる場合の observable。
    // パレット側でこれを購読し、通知時に CanExecute を再評価する。
    public IObservable<Unit>? StateChanged { get; init; }
}
