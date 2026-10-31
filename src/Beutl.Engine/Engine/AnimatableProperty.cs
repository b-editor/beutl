using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Beutl.Validation;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.Engine;

public class AnimatableProperty<T> : IExpressionProperty<T>, IExpressionChainHost, IChildObjectSource
{
    private T _currentValue;
    private IPropertyAnimation<T>? _keyFrames;
    private ExpressionEvaluationState<T>? _expressionState;
    private IValidator<T>? _validator;
    private Attribute[]? _attributes;
    private string? _name;
    private CoreObject? _owner;
    private PropertyLookup? _propertyLookup;

    public AnimatableProperty(T defaultValue, IValidator<T>? validator = null)
    {
        DefaultValue = defaultValue;
        _currentValue = defaultValue;
        _validator = validator;
        ValueType = typeof(T);
        IsAnimatable = true;
    }

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; }

    public bool IsAnimatable { get; }

    public bool IsReference => false;

    public T DefaultValue { get; internal set; }

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
            IsValueSet = true;

            ValueChanged?.Invoke(this, new PropertyValueChangedEventArgs<T>(this, oldValue, validatedValue));
            Edited?.Invoke(this, EventArgs.Empty);
            PropertyValueOwnership.Reparent(_owner, oldValue, validatedValue);

            if (oldValue is INotifyEdited oldEdited)
                oldEdited.Edited -= OnChildEdited;
            if (validatedValue is INotifyEdited newEdited)
                newEdited.Edited += OnChildEdited;
        }
    }

    public void SetValueEvenIfEqual(T value)
        => SetCurrentValue(value, replaceEquivalent: true);

    public bool IsValueSet { get; private set; }

    public bool HasExpression => Expression is not null and not IPropertyAnimation<T>;

    public string? ExpressionError => Volatile.Read(ref _expressionState)?.Error;

    public IExpression<T>? Expression
    {
        get => Volatile.Read(ref _expressionState)?.Expression;
        set => SetExpressionChain(value is null || ExpressionChain.FindKeyFrames(value) == null
            ? ExpressionChain.WithKeyFrames(value, _keyFrames)
            : value);
    }

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public event EventHandler? Edited;

    public event Action<IExpression<T>?>? ExpressionChanged;

    void IExpressionChainHost.SetExpressionChain(IExpression? chain) => SetExpressionChain((IExpression<T>?)chain);

    IEnumerable<object?> IChildObjectSource.GetChildObjects()
        => ChildObjects.MayHoldObjects(typeof(T)) ? [_currentValue, _keyFrames] : [_keyFrames];

    void IExpressionChainHost.SetKeyFrames(IExpression? keyFrames)
    {
        if (keyFrames is not null and not IPropertyAnimation<T>)
            throw new ArgumentException($"Property '{_name}' takes an animation of {typeof(T).Name}.", nameof(keyFrames));

        if (ReferenceEquals(keyFrames, _keyFrames)) return;
        IExpression<T>? chain = ExpressionChain.WithKeyFrames(Expression, (IPropertyAnimation<T>?)keyFrames);
        if (ExpressionChain.FindKeyFrames(chain) != keyFrames)
            throw new InvalidOperationException($"The expression on property '{_name}' cannot hold keyframes below it.");

        SetExpressionChain(chain);
    }

    private void SetExpressionChain(IExpression<T>? chain)
    {
        if (Expression == chain) return;

        Volatile.Write(ref _expressionState, chain == null ? null : new ExpressionEvaluationState<T>(chain));
        var keyFrames = ExpressionChain.FindKeyFrames(chain) as IPropertyAnimation<T>;
        IPropertyAnimation<T>? oldKeyFrames = _keyFrames;
        if (keyFrames != oldKeyFrames)
        {
            _keyFrames = keyFrames;
            keyFrames?.Configure(this);
        }

        ExpressionChanged?.Invoke(chain);
        if (keyFrames != oldKeyFrames)
        {
            PropertyValueOwnership.Reparent(_owner, oldKeyFrames, keyFrames);
            if (oldKeyFrames != null)
                oldKeyFrames.Edited -= OnChildEdited;
            if (keyFrames != null)
                keyFrames.Edited += OnChildEdited;
        }
        // Editors must see the tracks after attachment has established their time origin.
        Edited?.Invoke(this, EventArgs.Empty);
    }

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
        T value;

        // Keyframes on top are evaluated without an ExpressionContext.
        if (Volatile.Read(ref _expressionState) is { Expression: not KeyFrameAnimation<T> } expressionState)
        {
            _propertyLookup ??= new PropertyLookup(
                (_owner as IHierarchical)?.FindHierarchicalRoot() as ICoreObject ?? BeutlApplication.Current,
                _owner as IHierarchical);
            if (context is ExpressionContext expressionContext)
            {
                if (expressionContext.IsEvaluating(this))
                    return DefaultValue;
            }
            else
            {
                expressionContext = new ExpressionContext(context.Time, this, _propertyLookup);
            }

            expressionContext.BeginEvaluation(this);
            try
            {
                value = expressionState.Expression.Evaluate(expressionContext);
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
        if (_keyFrames is { } keyFrames)
        {
            value = keyFrames.EvaluateAt(context.Time) ?? _currentValue;
            value = ValidateAndCoerce(value);
        }
        else
        {
            value = _currentValue;
        }

        return value;
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
        _keyFrames?.Configure(this);
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

            if (_keyFrames != null)
                ownerHierarchical.AddChild(_keyFrames);
        }
        else if (_owner is IModifiableHierarchical oldOwnerHierarchical)
        {
            if (Value is IHierarchical hierarchical)
                oldOwnerHierarchical.RemoveChild(hierarchical);

            if (_keyFrames != null)
                oldOwnerHierarchical.RemoveChild(_keyFrames);
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
        IsValueSet = false;
        SetExpressionChain(null);
    }

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
            Value = optional.Value;
        }
    }

    public void SerializeValue(ICoreSerializationContext context)
    {
        context.SetValue(Name, Value);
    }

    public void DeserializeExpression(JsonNode expressionNode)
    {
        Expression = Expressions.Expression.CreateFromNode<T>(expressionNode);
    }

    // Keyframes are written under "Animations"; the rest of the chain is written here, as before.
    public JsonNode? SerializeExpression()
    {
        return Expression is { } expression and not IPropertyAnimation<T>
            ? Expressions.Expression.ToNode(expression)
            : null;
    }
}
