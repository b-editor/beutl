using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IToolContext : IDisposable, IJsonSerializable, IServiceProvider
{
    ToolTabExtension Extension { get; }

    IReactiveProperty<bool> IsSelected { get; }

    IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; }

    IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; }

    string Header { get; }
}

public abstract class ToolTabExtension : ViewExtension
{
    public enum TabPlacement
    {
        [Obsolete("Use 'BottomLeft' or 'BottomRight' instead.")]
        Bottom = 0,

        [Obsolete("Use 'BottomLeft' or 'BottomRight' instead.")]
        Right = 1,

        LeftUpperTop = 5,
        LeftUpperBottom = 2,
        LeftLowerTop = 6,
        LeftLowerBottom = 0,

        RightUpperTop = 3,
        RightUpperBottom = 1,
        RightLowerTop = 7,
        RightLowerBottom = 4,
    }

    public enum TabDisplayMode
    {
        Docked,
        Floating,
    }

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
