using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.Extensibility;

public enum ToolWindowMode
{
    Dialog,
    Window,
}

public interface IToolWindowContext : IDisposable
{
    ToolWindowExtension Extension { get; }

    string Header { get; }
}

public abstract class ToolWindowExtension : Extension
{
    public virtual ToolWindowMode Mode => ToolWindowMode.Dialog;

    public virtual bool CanMultiple => false;

    public virtual IconSource? GetIcon() => null;

    public abstract bool TryCreateContent([NotNullWhen(true)] out Window? window);

    public abstract bool TryCreateContext([NotNullWhen(true)] out IToolWindowContext? context);
}
