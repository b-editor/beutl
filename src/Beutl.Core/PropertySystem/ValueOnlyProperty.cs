using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Serialization;
using Beutl.Validation;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl;

// A property that holds only a value: it takes no expression, reference, node connection or keyframes.
public class ValueOnlyProperty<T>(T defaultValue, IValidator<T>? validator = null) : IProperty<T>, IChildObjectSource
{
    private IValidator<T>? _validator = validator;
    private T _currentValue = defaultValue;
    private Attribute[]? _attributes;
    private string? _name;
    private CoreObject? _owner;

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; } = typeof(T);

    public bool IsAnimatable => false;

    public bool SupportsExpression => false;

    public T DefaultValue { get; } = defaultValue;

    public T CurrentValue
    {
        get => _currentValue;
        set => SetCurrentValue(value, replaceEquivalent: false);
    }

    public bool HasLocalValue { get; private set; }

    public bool HasExpression => false;

    public IExpression<T>? Expression
    {
        get => null;
        set
        {
            if (value != null)
                throw new InvalidOperationException($"Property '{Name}' does not support expressions.");
        }
    }

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public event EventHandler? Edited;

    event Action<IExpression<T>?>? IProperty<T>.ExpressionChanged
    {
        add { }
        remove { }
    }

    private void SetCurrentValue(T value, bool replaceEquivalent)
    {
        DocumentWriteCheck.Verify(_owner, _name);
        var validatedValue = ValidateAndCoerce(value);
        if (ValueReplacement.RequiresReplacement(_currentValue, validatedValue, replaceEquivalent))
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

    public void operator <<=(T value)
    {
        CurrentValue = value;
    }

    private void OnChildEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    IEnumerable<object?> IChildObjectSource.GetChildObjects()
        => ChildObjects.MayHoldObjects(typeof(T)) ? [_currentValue] : [];

    public T GetValue(EvaluationContext context) => _currentValue;

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

    public CoreObject? GetOwnerObject() => _owner;

    private T ValidateAndCoerce(T value)
    {
        if (_validator == null)
            return value;

        var context = new ValidationContext(this, null);
        if (_validator.TryCoerce(context, ref value!))
            return value;

        return _validator.Validate(context, value) == null ? value : DefaultValue;
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
    }

    public JsonNode? SerializeExpression() => null;
}
