using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Beutl.Validation;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.Engine;

public class SimpleProperty<T>(T defaultValue, IValidator<T>? validator = null)
    : IProperty<T>
{
    private IValidator<T>? _validator = validator;
    private T _currentValue = defaultValue;
    private Attribute[]? _attributes;
    private string? _name;
    private EngineObject? _owner;
    private ExpressionEvaluationState<T>? _expressionState;
    private PropertyLookup? _propertyLookup;

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; } = typeof(T);

    public bool IsAnimatable { get; } = false;

    /// <summary>
    /// Gets whether the property takes an expression. A property whose value has no meaning that varies
    /// with time, such as one that shapes a structure, sets this to <see langword="false"/>.
    /// </summary>
    public bool SupportsExpression { get; init; } = true;

    public T DefaultValue { get; } = defaultValue;

    public T CurrentValue
    {
        get => _currentValue;
        set => SetCurrentValue(value, replaceEquivalent: false);
    }

    private void SetCurrentValue(T value, bool replaceEquivalent)
    {
        var validatedValue = ValidateAndCoerce(value);
        bool hasReplacement = ValueReplacement.RequiresReplacement(
            _currentValue,
            validatedValue,
            replaceEquivalent);
        if (hasReplacement)
        {
            var oldValue = _currentValue;
            _currentValue = validatedValue;
            HasLocalValue = true;

            ValueChanged?.Invoke(this, new PropertyValueChangedEventArgs<T>(this, oldValue, validatedValue));
            Edited?.Invoke(this, EventArgs.Empty);
            PropertyValueOwnership.Reparent(_owner, oldValue, validatedValue);

            if (oldValue is INotifyEdited oldEdited)
                oldEdited.Edited -= OnChildEdited;
            if (validatedValue is INotifyEdited newEdited)
                newEdited.Edited += OnChildEdited;
        }
    }

    public void ReplaceCurrentValue(T value)
        => SetCurrentValue(value, replaceEquivalent: true);

    public IAnimation<T>? Animation
    {
        get => null;
        set
        {
            if (value != null)
            {
                throw new InvalidOperationException(
                    $"Property '{Name}' does not support animations. Use Property.CreateAnimatable<T>() to create animatable properties.");
            }
        }
    }

    public IExpression<T>? Expression
    {
        get => Volatile.Read(ref _expressionState)?.Expression;
        set
        {
            if (value != null && !SupportsExpression)
            {
                throw new InvalidOperationException($"Property '{Name}' does not support expressions.");
            }

            if (Expression != value)
            {
                Volatile.Write(ref _expressionState, value == null ? null : new ExpressionEvaluationState<T>(value));
                ExpressionChanged?.Invoke(value);
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool HasLocalValue { get; private set; }

    public bool HasExpression => Volatile.Read(ref _expressionState) != null;

    public string? ExpressionError => Volatile.Read(ref _expressionState)?.Error;

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public event EventHandler? Edited;

    public event Action<IExpression<T>?>? ExpressionChanged;

    public void operator <<=(T value)
    {
        CurrentValue = value;
    }

    private void OnChildEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    public T GetValue(CompositionContext context)
    {
        if (Volatile.Read(ref _expressionState) is { } expressionState)
        {
            _propertyLookup ??= new PropertyLookup(
                _owner?.FindHierarchicalRoot() as ICoreObject ?? BeutlApplication.Current);

            ExpressionContext expressionContext;
            if (context is ExpressionContext ec)
            {
                if (ec.IsEvaluating(this))
                    return DefaultValue;
                expressionContext = ec;
            }
            else
            {
                expressionContext = new ExpressionContext(context.Time, this, _propertyLookup);
            }

            expressionContext.BeginEvaluation(this);
            try
            {
                T value = expressionState.Expression.Evaluate(expressionContext);
                expressionState.ClearError();
                return ValidateAndCoerce(value);
            }
            catch (ExpressionException ex)
            {
                expressionState.ReportFailure(_name, _owner, ex);
            }
            finally
            {
                expressionContext.EndEvaluation(this);
            }
        }

        return _currentValue;
    }

    public void SetAttributes(string name, Attribute[] attributes)
    {
        _name = name;
        _attributes = attributes;
    }

    public Attribute[]? GetAttributes() => _attributes;

    public IValidator CreateValidator(Attribute[] attributes)
    {
        IValidator<T>[] validations = Property.ConvertValidators<T>(attributes.OfType<ValidationAttribute>());

        return new MultipleValidator<T>(validations);
    }

    public void SetValidator(IValidator validator)
    {
        _validator = (IValidator<T>)validator;
    }

    public IValidator? GetValidator() => _validator;

    public void SetOwnerObject(EngineObject? owner)
    {
        if (_owner == owner) return;
        _propertyLookup = null;

        if (owner is IModifiableHierarchical ownerHierarchical)
        {
            if (CurrentValue is IHierarchical hierarchical)
                ownerHierarchical.AddChild(hierarchical);
        }
        else if (_owner is IModifiableHierarchical oldOwnerHierarchical)
        {
            if (CurrentValue is IHierarchical hierarchical)
                oldOwnerHierarchical.RemoveChild(hierarchical);
        }

        _owner = owner;
    }

    public EngineObject? GetOwnerObject()
    {
        return _owner;
    }


    private T ValidateAndCoerce(T value)
    {
        if (_validator == null)
            return value;

        var context = new ValidationContext(this, null);
        if (_validator.TryCoerce(context, ref value!))
        {
            return value;
        }

        // Coerceできなかった場合、Validateを試みる
        if (_validator.Validate(context, value) == null)
        {
            return value;
        }

        if (_validator.Validate(new(this, null), value) == null)
        {
            return value;
        }

        return DefaultValue;
    }

    public void ResetToDefault()
    {
        CurrentValue = DefaultValue;
        HasLocalValue = false;
        Expression = null;
    }

    public bool HasValidator => _validator != null;

    public override string ToString() =>
        Expression is { } expression
            ? $"{Name}: {_currentValue} (Default: {DefaultValue}, Simple, Expression: {expression.ExpressionString})"
            : $"{Name}: {_currentValue} (Default: {DefaultValue}, Simple)";

    public void DeserializeValue(ICoreSerializationContext context)
    {
        var optional = context.GetValue<Optional<T>>(Name);
        if (optional.HasValue)
        {
            if (optional.Value is IReference { IsNull: false } reference)
            {
                context.Resolve(reference.Id,
                    resolved => CurrentValue = (T)reference.Resolved((CoreObject)resolved));
            }

            CurrentValue = optional.Value;
        }
    }

    public void SerializeValue(ICoreSerializationContext context)
    {
        context.SetValue(Name, CurrentValue);
    }

    public void DeserializeExpression(JsonNode expressionNode)
    {
        if (!SupportsExpression) return;

        Expression = Expressions.Expression.CreateFromNode<T>(expressionNode);
    }

    public JsonNode? SerializeExpression()
    {
        return Expression == null ? null : Expressions.Expression.ToNode(Expression);
    }
}

public static class SimplePropertyExtensions
{
    public static IProperty<T> ToAnimatable<T>(this SimpleProperty<T> simpleProperty)
    {
        var animatableProperty = Property.CreateAnimatable(
            simpleProperty.DefaultValue);

        // 現在値を引き継ぎ
        if (simpleProperty.HasLocalValue)
        {
            animatableProperty.CurrentValue = simpleProperty.CurrentValue;
        }

        return animatableProperty;
    }
}
