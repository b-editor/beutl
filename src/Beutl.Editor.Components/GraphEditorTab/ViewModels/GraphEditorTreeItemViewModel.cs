using Avalonia.Media;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using FluentIcons.Common;
using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed class GraphEditorTreeItemViewModel : IDisposable
{
    internal GraphEditorTreeItemViewModel(string key, GraphEditorTreeItemViewModel? parent)
    {
        Key = key;
        Parent = parent;
        IsExpanded.Value = parent == null;
        SymbolIconVariant = HasKeyFrame.Select(hasKeyFrame => hasKeyFrame ? IconVariant.Filled : IconVariant.Regular)
            .ToReadOnlyReactivePropertySlim();
        AnimationToolTip = HasAnimation.CombineLatest(HasKeyFrame, (animated, hasKeyFrame) => !animated
                ? Strings.EnableAnimation
                : $"{(hasKeyFrame ? CommandNames.RemoveKeyFrame : CommandNames.InsertKeyFrame)}\n{MessageStrings.RightClickToShowMenu}")
            .ToReadOnlyReactivePropertySlim(Strings.EnableAnimation);
    }

    internal string Key { get; }
    internal IProperty? Property { get; set; }
    internal IPropertyAdapter? Adapter { get; set; }
    internal string? ChannelName { get; set; }
    internal GraphEditorTreeItemViewModel? PropertyItem { get; set; }
    public GraphEditorTreeItemViewModel? Parent { get; }
    public CoreList<GraphEditorTreeItemViewModel> Children { get; } = [];
    public ReactivePropertySlim<string> Name { get; } = new(string.Empty);
    public ReactivePropertySlim<bool> IsExpanded { get; } = new();
    public ReactivePropertySlim<bool> CanAnimate { get; } = new();
    public ReactivePropertySlim<bool> HasAnimation { get; } = new();
    public ReactivePropertySlim<bool> HasKeyFrame { get; } = new();
    public ReadOnlyReactivePropertySlim<IconVariant> SymbolIconVariant { get; }
    public ReadOnlyReactivePropertySlim<string> AnimationToolTip { get; }
    public ReactivePropertySlim<IBrush?> ChannelBrush { get; } = new();

    internal Type? ValueType => Property?.ValueType ?? Adapter?.PropertyType;
    internal object? Value => Property != null ? Property.CurrentValue : Adapter?.GetValue();
    internal IAnimation? Animation
    {
        get => Property != null ? Property.Animation : (Adapter as IAnimatablePropertyAdapter)?.Animation;
        set
        {
            if (Property != null) Property.Animation = value;
            else if (Adapter is IAnimatablePropertyAdapter adapter) adapter.Animation = value;
        }
    }

    internal void ClearExpression()
    {
        if (Property is { SupportsExpression: true }) Property.Expression = null;
        else if (Adapter is IExpressionPropertyAdapter adapter) adapter.Expression = null;
    }

    public void Dispose()
    {
        Name.Dispose();
        IsExpanded.Dispose();
        CanAnimate.Dispose();
        HasAnimation.Dispose();
        HasKeyFrame.Dispose();
        SymbolIconVariant.Dispose();
        AnimationToolTip.Dispose();
        ChannelBrush.Dispose();
    }
}
