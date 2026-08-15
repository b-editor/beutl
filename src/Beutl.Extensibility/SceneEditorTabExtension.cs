using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IToolContext : IDisposable, IJsonSerializable, IServiceProvider
{
    ToolTabExtension Extension { get; }

    IReactiveProperty<bool> IsSelected { get; }

    /// <summary>
    /// Gets the text shown on this tool's dock tab.
    /// </summary>
    /// <remarks>
    /// Push a new value whenever the tab's identity changes — the folder a file browser is showing,
    /// the element a graph editor is editing — so that several instances of a
    /// <see cref="ToolTabExtension.CanMultiple"/> tool stay distinguishable. Values must be produced
    /// on the UI thread; the host binds this straight onto the dockable's title. This is the
    /// per-instance title, unlike <see cref="ToolTabExtension.Header"/>.
    /// </remarks>
    IReadOnlyReactiveProperty<string> Header { get; }
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

    /// <summary>
    /// Gets the label this tool takes in the "add tool tab" menu, or <see langword="null"/> to keep
    /// it out of that menu entirely.
    /// </summary>
    /// <remarks>
    /// Static per-extension metadata. The title of an open tab comes from
    /// <see cref="IToolContext.Header"/> instead.
    /// </remarks>
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
