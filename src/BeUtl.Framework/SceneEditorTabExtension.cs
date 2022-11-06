using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Reactive.Bindings;

namespace Beutl.Framework;

public interface IToolContext : IDisposable, IJsonSerializable
{
    ToolTabExtension Extension { get; }

    IReactiveProperty<bool> IsSelected { get; }

    IReadOnlyReactiveProperty<string> Header { get; }

    ToolTabExtension.TabPlacement Placement { get; }
}

public abstract class ToolTabExtension : ViewExtension
{
    public enum TabPlacement
    {
        Bottom,
        Right
    }

    public abstract bool CanMultiple { get; }

    public virtual IObservable<string>? Header => null;

    public abstract bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IControl? control);

    public abstract bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context);
}
