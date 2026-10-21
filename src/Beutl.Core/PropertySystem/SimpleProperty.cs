using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Serialization;
using Beutl.Validation;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl;

public class SimpleProperty<T>(T defaultValue, IValidator<T>? validator = null)
    : IExpressionProperty<T>, IExpressionChainHost, IChildObjectSource
{
    private IValidator<T>? _validator = validator;
    private T _currentValue = defaultValue;
    private Attribute[]? _attributes;
    private string? _name;
    private CoreObject? _owner;
    private ExpressionEvaluationState<T>? _expressionState;
    private PropertyLookup? _propertyLookup;

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; } = typeof(T);

    public bool IsAnimatable { get; } = false;

    public bool IsReference { get; init; }

    public T DefaultValue { get; } = defaultValue;

    public T Value
    {
        get => _currentValue;
        set => SetCurrentValue(value, replaceEquivalent: false);
    }

    private void SetCurrentValue(T value, bool replaceEquivalent)
    {
        DocumentWriteCheck.Verify(_owner, _name);
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

    public IExpression<T>? Expression
    {
        get => Volatile.Read(ref _expressionState)?.Expression;
        set
        {
            if (ExpressionChain.FindKeyFrames(value) != null)
            {
                throw new InvalidOperationException(
                    $"Property '{Name}' does not support animations. Use Property.CreateAnimatable<T>() to create animatable properties.");
            }

            if (Expression != value)
            {
                Volatile.Write(ref _expressionState, value == null ? null : new ExpressionEvaluationState<T>(value));
                ExpressionChanged?.Invoke(value);
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    void IExpressionChainHost.SetExpressionChain(IExpression? chain) => Expression = (IExpression<T>?)chain;

    IEnumerable<object?> IChildObjectSource.GetChildObjects()
        => ChildObjects.MayHoldObjects(typeof(T)) ? [_currentValue] : [];

    void IExpressionChainHost.SetKeyFrames(IExpression? keyFrames)
    {
        if (keyFrames != null) Expression = (IExpression<T>)keyFrames;
    }

    public bool HasLocalValue { get; private set; }

    public bool HasExpression => Volatile.Read(ref _expressionState) != null;

    public string? ExpressionError => Volatile.Read(ref _expressionState)?.Error;

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public event EventHandler? Edited;

    public event Action<IExpression<T>?>? ExpressionChanged;

    public void operator <<=(T value)
    {
        Value = value;
    }

    private void OnChildEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    public T Evaluate(EvaluationContext context)
    {
        if (Volatile.Read(ref _expressionState) is { } expressionState)
        {
            _propertyLookup ??= new PropertyLookup(
                (_owner as IHierarchical)?.FindHierarchicalRoot() as ICoreObject ?? BeutlApplication.Current);

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
        IValidator<T>[] validations = PropertyValidators.FromAttributes<T>(attributes.OfType<ValidationAttribute>());

        return new MultipleValidator<T>(validations);
    }

    public void SetValidator(IValidator validator)
    {
        _validator = (IValidator<T>)validator;
    }

    public IValidator? GetValidator() => _validator;

    public void SetOwnerObject(CoreObject? owner)
    {
        if (_owner == owner) return;
        _propertyLookup = null;

        if (owner is IModifiableHierarchical ownerHierarchical)
        {
            if (Value is IHierarchical hierarchical)
                ownerHierarchical.AddChild(hierarchical);
        }
        else if (_owner is IModifiableHierarchical oldOwnerHierarchical)
        {
            if (Value is IHierarchical hierarchical)
                oldOwnerHierarchical.RemoveChild(hierarchical);
        }

        _owner = owner;
    }

    public CoreObject? GetOwnerObject()
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

        return DefaultValue;
    }

    public void ResetToDefault()
    {
        Value = DefaultValue;
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
        if (context is JsonSerializationContext jsonContext
            && jsonContext.TryGetPreservedValue(Name, typeof(T), out _, allowCollections: true))
        {
            return;
        }

        var optional = context.GetValue<Optional<T>>(FormerPersistedNames.ResolveMemberKey(context, Name, _attributes));
        if (optional.HasValue)
        {
            if (optional.Value is IReference { IsNull: false } reference)
            {
                context.Resolve(reference.Id,
                    resolved => Value = (T)reference.Resolved((CoreObject)resolved));
            }

            Value = optional.Value;
        }
    }

    public void SerializeValue(ICoreSerializationContext context)
    {
        context.SetValue(Name, Value);
    }

    public void DeserializeExpression(JsonNode expressionNode)
    {
        if (_owner is not IExpressionJsonCodec codec) return;

        Expression = codec.Read<T>(expressionNode);
    }

    public JsonNode? SerializeExpression()
    {
        return Expression is { } expression && _owner is IExpressionJsonCodec codec ? codec.Write(expression) : null;
    }
}
