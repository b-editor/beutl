using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.PathEditorTab.ViewModels;

// Change only the visible label. The owner, property identity, expressions, animation,
// defaults and notifications remain those of the real property, including across segments.
internal class PathPointPropertyAdapter(IProperty<Point> property, EngineObject owner, string label)
    : EnginePropertyAdapter<Point>(property, owner), IPropertyAdapter<Point>
{
    protected string Label { get; } = label;
    string IPropertyAdapter.DisplayName => Label;
    IProperty IPropertyAdapter.GetEngineProperty() => Property;
}

internal sealed class AnimatedPathPointPropertyAdapter(AnimatableProperty<Point> property, EngineObject owner, string label)
    : PathPointPropertyAdapter(property, owner, label), IAnimatablePropertyAdapter<Point>
{
    private readonly AnimatablePropertyAdapter<Point> _inner = new(property, owner);

    string IPropertyAdapter.DisplayName => Label;
    IProperty IPropertyAdapter.GetEngineProperty() => Property;

    public IAnimation<Point>? Animation { get => _inner.Animation; set => _inner.Animation = value; }
    public IObservable<IAnimation<Point>?> ObserveAnimation => _inner.ObserveAnimation;
    public IExpression<Point>? Expression { get => _inner.Expression; set => _inner.Expression = value; }
    public bool HasExpression => _inner.HasExpression;
    public IObservable<IExpression<Point>?> ObserveExpression => _inner.ObserveExpression;
}
