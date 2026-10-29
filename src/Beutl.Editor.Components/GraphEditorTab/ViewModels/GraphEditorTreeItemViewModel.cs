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
        ShowAnimationButton = CanAnimate.CombineLatest(CanRemoveAnimation, (canAnimate, canRemove) => canAnimate || canRemove)
            .ToReadOnlyReactivePropertySlim();
        AnimationToolTip = HasAnimation.CombineLatest(HasKeyFrame, CanAnimate, (animated, hasKeyFrame, canAnimate) => !canAnimate
                ? Strings.RemoveAnimation
                : !animated ? Strings.EnableAnimation
                    : $"{(hasKeyFrame ? CommandNames.RemoveKeyFrame : CommandNames.InsertKeyFrame)}\n{MessageStrings.RightClickToShowMenu}")
            .ToReadOnlyReactivePropertySlim(Strings.EnableAnimation);
        SeparationToolTip = HasSeparatedComponents.Select(separated => separated
                ? $"{Strings.UnseparateComponents}\n{Strings.UnseparateComponents_Description}"
                : Strings.SeparateComponents)
            .ToReadOnlyReactivePropertySlim(Strings.SeparateComponents);
    }

    internal string Key { get; }
    internal IProperty? Property { get; set; }
    internal IPropertyAdapter? Adapter { get; set; }
    internal EngineObject? AnimationOwner { get; set; }
    internal string? ChannelName { get; set; }
    internal GraphEditorTreeItemViewModel? PropertyItem { get; set; }
    public GraphEditorTreeItemViewModel? Parent { get; }
    public CoreList<GraphEditorTreeItemViewModel> Children { get; } = [];
    public ReactivePropertySlim<string> Name { get; } = new(string.Empty);
    public ReactivePropertySlim<bool> IsExpanded { get; } = new();
    public ReactivePropertySlim<bool> CanAnimate { get; } = new();
    public ReactivePropertySlim<bool> CanRemoveAnimation { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> ShowAnimationButton { get; }
    public ReactivePropertySlim<bool> HasAnimation { get; } = new();
    public ReactivePropertySlim<bool> HasKeyFrame { get; } = new();
    public ReadOnlyReactivePropertySlim<IconVariant> SymbolIconVariant { get; }
    public ReadOnlyReactivePropertySlim<string> AnimationToolTip { get; }
    public ReactivePropertySlim<bool> CanSeparateComponents { get; } = new();
    public ReactivePropertySlim<bool> HasSeparatedComponents { get; } = new();
    public ReadOnlyReactivePropertySlim<string> SeparationToolTip { get; }
    public ReactivePropertySlim<IBrush?> ChannelBrush { get; } = new();

    internal Type? ValueType => Property?.ValueType ?? Adapter?.PropertyType;
    internal object? Value => Property != null ? Property.Value : Adapter?.GetValue();
    internal IKeyFrameAnimation? Animation
    {
        get => Property != null ? Property.GetKeyFrameAnimation() : (Adapter as IAnimatablePropertyAdapter)?.Animation;
        set
        {
            if (Property != null) Property.SetKeyFrameAnimation(value);
            else if (Adapter is IAnimatablePropertyAdapter adapter) adapter.Animation = value;
        }
    }

    internal void ClearExpression()
    {
        if (Property is IExpressionProperty expressionProperty) expressionProperty.Expression = null;
        else if (Adapter is IExpressionPropertyAdapter adapter) adapter.Expression = null;
    }

    public void Dispose()
    {
        Name.Dispose();
        IsExpanded.Dispose();
        CanAnimate.Dispose();
        CanRemoveAnimation.Dispose();
        ShowAnimationButton.Dispose();
        HasAnimation.Dispose();
        HasKeyFrame.Dispose();
        SymbolIconVariant.Dispose();
        AnimationToolTip.Dispose();
        CanSeparateComponents.Dispose();
        HasSeparatedComponents.Dispose();
        SeparationToolTip.Dispose();
        ChannelBrush.Dispose();
    }
}
