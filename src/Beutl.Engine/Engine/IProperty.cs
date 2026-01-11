using System.Reflection;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

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

    object? CurrentValue { get; set; }

    string Name { get; }

    Type ValueType { get; }

    IAnimation? Animation { get; set; }

    IExpression? Expression { get; set; }

    bool IsAnimatable { get; }

    bool HasLocalValue { get; }

    bool HasExpression { get; }

    void SetPropertyInfo(PropertyInfo propertyInfo);

    PropertyInfo? GetPropertyInfo();

    void SetOwnerObject(EngineObject? owner);

    EngineObject? GetOwnerObject();

    void DeserializeValue(ICoreSerializationContext context);

    void SerializeValue(ICoreSerializationContext context);

    IValidator CreateValidator(PropertyInfo propertyInfo);

    void SetValidator(IValidator validator);
}

public interface IProperty<T> : IProperty
{
    new T DefaultValue { get; }

    new T CurrentValue { get; set; }

    new IAnimation<T>? Animation { get; set; }

    new IExpression<T>? Expression { get; set; }

    object? IProperty.DefaultValue => DefaultValue;

    IAnimation? IProperty.Animation
    {
        get => Animation;
        set => Animation = (IAnimation<T>?)value;
    }

    IExpression? IProperty.Expression
    {
        get => Expression;
        set => Expression = (IExpression<T>?)value;
    }

    object? IProperty.CurrentValue
    {
        get => CurrentValue;
        set
        {
            if (value is T tValue)
            {
                CurrentValue = tValue;
            }
            else if (value == null && !typeof(T).IsValueType)
            {
                CurrentValue = default!;
            }
            else
            {
                throw new InvalidCastException();
            }
        }
    }

    T GetValue(RenderContext context);

    event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    event Action<IExpression<T>?>? ExpressionChanged;

    void operator <<=(T value);
}

public class PropertyValueChangedEventArgs<T>(IProperty<T> property, T oldValue, T newValue) : EventArgs
{
    public IProperty<T> Property { get; } = property;

    public T OldValue { get; } = oldValue;

    public T NewValue { get; } = newValue;
}
