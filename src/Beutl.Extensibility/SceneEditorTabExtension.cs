using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IToolContext : IDisposable, IJsonSerializable, IServiceProvider
{
    ToolTabExtension Extension { get; }

    IReactiveProperty<bool> IsSelected { get; }

    string Header { get; }
}

public abstract class ToolTabExtension : ViewExtension
{
    public abstract bool CanMultiple { get; }

    /// <summary>
    /// Gets whether the host reuses the same content control when this tool is deactivated and
    /// activated again.
    /// </summary>
    /// <remarks>
    /// Reused controls can still be unloaded from and loaded into the visual tree. State and
    /// resources that must survive those transitions and require deterministic cleanup should be
    /// owned by the <see cref="IToolContext"/>, which is disposed when its dockable closes.
    /// </remarks>
    public virtual bool ReuseContentAcrossActivation => false;

    public virtual string? Header => null;

    public virtual DockAnchor DefaultAnchor => DockAnchor.None;

    public virtual int DefaultOrder => 0;

    public virtual bool OpenByDefault => false;

    public abstract bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control);

    public abstract bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context);
}
