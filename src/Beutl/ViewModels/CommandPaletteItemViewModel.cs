using Avalonia.Input;
using Beutl.Services;

namespace Beutl.ViewModels;

public sealed class CommandPaletteItemViewModel
{
    public CommandPaletteItemViewModel(PaletteCommand command, bool isEnabled, int relevance)
    {
        Command = command;
        IsEnabled = isEnabled;
        Relevance = relevance;
    }

    public PaletteCommand Command { get; }

    public string DisplayName => Command.DisplayName;

    public string? Description => Command.Description;

    public string CategoryName => Command.CategoryName;

    public KeyGesture? KeyGesture => Command.KeyGesture;

    public string? KeyGestureText => Command.KeyGesture?.ToString();

    public bool IsEnabled { get; }

    internal int Relevance { get; }

    public void Execute()
    {
        if (IsEnabled)
        {
            Command.Execute();
        }
    }
}
