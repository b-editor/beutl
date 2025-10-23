using System.Reflection;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Serialization;

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

    string Name { get; }

    Type ValueType { get; }

    IAnimation? Animation { get; set; }

    bool IsAnimatable { get; }

    bool HasLocalValue { get; }

    void SetPropertyInfo(PropertyInfo propertyInfo);

    PropertyInfo? GetPropertyInfo();

    void SetOwnerObject(EngineObject? owner);

    EngineObject? GetOwnerObject();

    void DeserializeValue(ICoreSerializationContext context);

    void SerializeValue(ICoreSerializationContext context);
}

public interface IProperty<T> : IProperty
{
    new T DefaultValue { get; }

    T CurrentValue { get; set; }

    new IAnimation<T>? Animation { get; set; }

    object? IProperty.DefaultValue => DefaultValue;

    IAnimation? IProperty.Animation
    {
        get => Animation;
        set => Animation = (IAnimation<T>?)value;
    }

    T GetValue(TimeSpan time);

    event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    void operator <<=(T value);
}

public class PropertyValueChangedEventArgs<T>(IProperty<T> property, T oldValue, T newValue) : EventArgs
{
    public IProperty<T> Property { get; } = property;

    public T OldValue { get; } = oldValue;

    public T NewValue { get; } = newValue;
}
