using Avalonia.Input;

namespace Beutl.Services;

public sealed record PaletteCommand(
    string Id,
    string DisplayName,
    string? Description,
    string CategoryName,
    KeyGesture? KeyGesture,
    Func<bool> CanExecute,
    Action Execute);
