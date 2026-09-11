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

    /// <summary>Gets the tool window icon using FluentAvalonia 3's icon source contract.</summary>
    /// <remarks>
    /// The Avalonia 12 host requires extensions to be rebuilt against the matching Beutl SDK.
    /// Overrides compiled with FluentAvalonia 2's IconSource return type are not binary compatible.
    /// See docs/extension-authoring/avalonia-12-migration.md for the migration steps.
    /// </remarks>
    public virtual FAIconSource? GetIcon() => null;

    public abstract bool TryCreateContent([NotNullWhen(true)] out Window? window);

    public abstract bool TryCreateContext([NotNullWhen(true)] out IToolWindowContext? context);
}
