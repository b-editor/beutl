using System.ComponentModel;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Operation;

public abstract class PublishOperator<T> : SourceOperator, IPublishOperator
    where T : EngineObject, new()
{
    public static readonly CoreProperty<T> ValueProperty;
    private Element? _element;
    private bool _isDeserializing;

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
    }

    protected void AddProperty<TProperty>(IProperty<TProperty> property, Optional<TProperty> defaultValue = default)
    {
        if (!_isDeserializing && defaultValue.HasValue)
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
        if (args is CorePropertyChangedEventArgs e)
        {
            if (e.Property.Id == ValueProperty.Id)
            {
                RaiseEdited(this, EventArgs.Empty);
                if (e.OldValue is INotifyEdited oldValue)
                {
                    oldValue.Edited -= OnValueEdited;
                }

                if (e.NewValue is INotifyEdited newValue)
                {
                    newValue.Edited += OnValueEdited;
                }

                Properties.Clear();
                FillProperties();
            }
            else if (e.Property.Id == IsEnabledProperty.Id && _element != null)
            {
                Value.IsEnabled = _element.IsEnabled && IsEnabled;
            }
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        try
        {
            _isDeserializing = true;
            base.Deserialize(context);
            UpdateValueFromElement();
        }
        finally
        {
            _isDeserializing = false;
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        _element = this.FindHierarchicalParent<Element>();
        if (_element != null)
        {
            _element.PropertyChanged += OnParentElementPropertyChanged;
            UpdateValueFromElement();
        }
    }

    private void UpdateValueFromElement()
    {
        if (_element == null) return;

        Value.IsTimeAnchor = true;
        Value.ZIndex = _element.ZIndex;
        Value.TimeRange = new TimeRange(_element.Start, _element.Length);
        Value.IsEnabled = _element.IsEnabled && IsEnabled;
    }

    private void OnParentElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs args && _element != null)
        {
            if (args.Property.Id == Element.IsEnabledProperty.Id)
            {
                Value.IsEnabled = _element.IsEnabled && IsEnabled;
            }
            else if (args.Property.Id == Element.ZIndexProperty.Id)
            {
                Value.ZIndex = _element.ZIndex;
            }
            else if (args.Property.Id == Element.StartProperty.Id || 
                     args.Property.Id == Element.LengthProperty.Id)
            {
                Value.TimeRange = new TimeRange(_element.Start, _element.Length);
            }
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        if (_element != null)
        {
            _element.PropertyChanged -= OnParentElementPropertyChanged;
        }

        _element = null;
    }

    private void OnValueEdited(object? sender, EventArgs e)
    {
        RaiseEdited(sender, e);
    }
}
