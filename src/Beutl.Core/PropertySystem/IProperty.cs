using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl;

public interface IListProperty : IProperty, ICoreList
{
    Type ElementType { get; }
}

public interface IListProperty<T> : IListProperty, IProperty<ICoreList<T>>, ICoreList<T>
{
    new CoreList<T>.Enumerator GetEnumerator();
}

public interface IProperty : INotifyEdited
{
    object? DefaultValue { get; }

    object? Value { get; set; }

    /// <summary>
    /// Validates and installs <paramref name="value"/>, replacing distinct reference-type values
    /// even when they compare equal while preserving the normal notification semantics.
    /// </summary>
    void ReplaceCurrentValue(object? value);

    string Name { get; }

    Type ValueType { get; }

    bool HasLocalValue { get; }

    void SetAttributes(string name, Attribute[] attributes);

    Attribute[]? GetAttributes();

    void SetOwnerObject(CoreObject? owner);

    CoreObject? GetOwnerObject();

    void DeserializeValue(ICoreSerializationContext context);

    void SerializeValue(ICoreSerializationContext context);

    IValidator CreateValidator(Attribute[] attributes);

    void SetValidator(IValidator validator);

    /// <summary>
    /// Gets the validator currently attached to this property.
    /// </summary>
    /// <remarks>
    /// This can differ from <see cref="CreateValidator"/> when a library or plugin supplies a custom validator.
    /// </remarks>
    IValidator? GetValidator();
}

public interface IProperty<T> : IProperty
{
    new T DefaultValue { get; }

    new T Value { get; set; }

    void ReplaceCurrentValue(T value);

    object? IProperty.DefaultValue => DefaultValue;

    object? IProperty.Value
    {
        get => Value;
        set
        {
            if (value is T tValue)
            {
                Value = tValue;
            }
            else if (value == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) is not null))
            {
                Value = default!;
            }
            else
            {
                throw new InvalidCastException();
            }
        }
    }

    void IProperty.ReplaceCurrentValue(object? value)
    {
        if (value is T typed)
        {
            ReplaceCurrentValue(typed);
        }
        else if (value is null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) is not null))
        {
            ReplaceCurrentValue(default!);
        }
        else
        {
            throw new InvalidCastException();
        }
    }

    event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    void operator <<=(T value);
}

// A property that also takes an expression: a user expression, a reference, a node connection or keyframes.
public interface IExpressionProperty : IProperty
{
    // The top of the expression chain; setting one without keyframes keeps the property's keyframes below it.
    IExpression? Expression { get; set; }

    bool IsAnimatable { get; }

    // Whether the expression slot holds a reference to another object, as Property.CreateReference<T>() declares.
    bool IsReference { get; }

    // Whether an expression other than keyframes is on top.
    bool HasExpression { get; }

    /// <summary>
    /// Gets the latest expression evaluation error, cleared after a successful evaluation or
    /// when the expression changes. This transient diagnostic is not a document edit.
    /// </summary>
    string? ExpressionError => null;

    void DeserializeExpression(JsonNode expressionNode);

    JsonNode? SerializeExpression();
}

public interface IExpressionProperty<T> : IExpressionProperty, IProperty<T>
{
    new IExpression<T>? Expression { get; set; }

    IExpression? IExpressionProperty.Expression
    {
        get => Expression;
        set => Expression = (IExpression<T>?)value;
    }

    T Evaluate(EvaluationContext context);

    event Action<IExpression<T>?>? ExpressionChanged;
}

public class PropertyValueChangedEventArgs<T>(IProperty<T> property, T oldValue, T newValue) : EventArgs
{
    public IProperty<T> Property { get; } = property;

    public T OldValue { get; } = oldValue;

    public T NewValue { get; } = newValue;
}
