using Avalonia.Media;
using Beutl.Animation;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
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
        ShowStandaloneExpressionBadge = HasExpression.CombineLatest(ShowAnimationButton, (hasExpression, showButton) => hasExpression && !showButton)
            .ToReadOnlyReactivePropertySlim();
        AnimationToolTip = HasAnimation.CombineLatest(HasKeyFrame, CanAnimate, HasExpression,
            (animated, hasKeyFrame, canAnimate, hasExpression) =>
            {
                string tip = !canAnimate
                    ? Strings.RemoveAnimation
                    : !animated ? Strings.EnableAnimation
                        : $"{(hasKeyFrame ? CommandNames.RemoveKeyFrame : CommandNames.InsertKeyFrame)}\n{MessageStrings.RightClickToShowMenu}";
                return hasExpression ? $"{tip}\n{MessageStrings.ExpressionIsSet}" : tip;
            })
            .ToReadOnlyReactivePropertySlim(Strings.EnableAnimation);
        SeparationToolTip = HasSeparatedComponents.Select(separated => separated
                ? $"{Strings.UnseparateComponents}\n{(Adapter is PropertyPair ? Strings.UnseparateComponents_SameTiming : Strings.UnseparateComponents_Description)}"
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
    public ReadOnlyReactivePropertySlim<bool> ShowStandaloneExpressionBadge { get; }
    public ReactivePropertySlim<bool> HasAnimation { get; } = new();
    public ReactivePropertySlim<bool> HasExpression { get; } = new();
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

    public void Dispose()
    {
        Name.Dispose();
        IsExpanded.Dispose();
        CanAnimate.Dispose();
        CanRemoveAnimation.Dispose();
        ShowAnimationButton.Dispose();
        ShowStandaloneExpressionBadge.Dispose();
        HasAnimation.Dispose();
        HasExpression.Dispose();
        HasKeyFrame.Dispose();
        SymbolIconVariant.Dispose();
        AnimationToolTip.Dispose();
        CanSeparateComponents.Dispose();
        HasSeparatedComponents.Dispose();
        SeparationToolTip.Dispose();
        ChannelBrush.Dispose();
    }
}
