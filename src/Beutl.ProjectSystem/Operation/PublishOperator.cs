using System.ComponentModel;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Operation;

public abstract class PublishOperator<T> : SourceOperator, IPublishOperator
    where T : EngineObject, new()
{
    public static readonly CoreProperty<T> ValueProperty;
    private Element? _element;

    private readonly EvaluationTarget _evaluationTarget =
        typeof(T).IsAssignableTo(typeof(Drawable)) ? EvaluationTarget.Graphics
        : typeof(T).IsAssignableTo(typeof(Audio.Sound)) ? EvaluationTarget.Audio : EvaluationTarget.Unknown;

    static PublishOperator()
    {
        ValueProperty = ConfigureProperty<T, PublishOperator<T>>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .Register();

        Hierarchy<PublishOperator<T>>(ValueProperty);
    }

    protected PublishOperator()
    {
        Value = new T();
    }

    public T Value
    {
        get;
        set => SetAndRaise(ValueProperty, ref field, value);
    }

    EngineObject IPublishOperator.Value => Value;

    public override EvaluationTarget GetEvaluationTarget() => _evaluationTarget;

    public override void Evaluate(OperatorEvaluationContext context)
    {
        context.AddFlowRenderable(Value);
        if (_element == null) return;

        // TODO: IsTimeAnchorをtrueにする
        Value.ZIndex = _element.ZIndex;
        Value.TimeRange = new TimeRange(_element.Start, _element.Length);
        Value.IsEnabled = _element.IsEnabled;
    }

    protected void AddProperty<TProperty>(IProperty<TProperty> property, Optional<TProperty> defaultValue = default)
    {
        if (defaultValue.HasValue)
        {
            property.CurrentValue = defaultValue.Value;
        }

        var adapter = property.IsAnimatable
            ? (IPropertyAdapter)new AnimatablePropertyAdapter<TProperty>((AnimatableProperty<TProperty>)property, Value)
            : new EnginePropertyAdapter<TProperty>(property, Value);
        Properties.Add(adapter);
    }

    protected virtual void FillProperties()
    {
        foreach (var property in Value.Properties)
        {
            var adapterType = property.IsAnimatable
                ? typeof(AnimatablePropertyAdapter<>).MakeGenericType(property.ValueType)
                : typeof(EnginePropertyAdapter<>).MakeGenericType(property.ValueType);

            var adapter = (IPropertyAdapter)Activator.CreateInstance(adapterType, property, Value)!;
            Properties.Add(adapter);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs<T?> e
            && e.Property.Id == ValueProperty.Id)
        {
            RaiseInvalidated(new RenderInvalidatedEventArgs(this, nameof(Value)));
            if (e.OldValue is IAffectsRender oldValue)
            {
                oldValue.Invalidated -= OnValueInvalidated;
            }

            if (e.NewValue is IAffectsRender newValue)
            {
                newValue.Invalidated += OnValueInvalidated;
            }

            Properties.Clear();
            FillProperties();
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        _element = this.FindHierarchicalParent<Element>();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        _element = null;
    }

    private void OnValueInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }
}
