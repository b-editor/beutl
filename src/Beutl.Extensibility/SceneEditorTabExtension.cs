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

    public virtual string? Header => null;

    public abstract IconSource GetIcon();

    public abstract bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control);

    public abstract bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context);
}
