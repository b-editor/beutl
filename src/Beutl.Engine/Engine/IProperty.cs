using System.Reflection;
using Beutl.Animation;
using Beutl.Collections;

namespace Beutl.Engine;

public interface IListProperty : IProperty, ICoreList
{
    Type ElementType { get; }
}

public interface IListProperty<T> : IListProperty, IProperty<ICoreList<T>>, ICoreList<T>
{
    new CoreList<T>.Enumerator GetEnumerator();
}

public interface IProperty
{
    string Name { get; }

    Type ValueType { get; }

    bool IsAnimatable { get; }

    bool HasLocalValue { get; }

    object? GetValueAsObject(IClock clock);

    void SetValueAsObject(object? value);

    object? GetDefaultValueAsObject();

    void SetPropertyInfo(PropertyInfo propertyInfo);

    PropertyInfo? GetPropertyInfo();

    void SetOwnerObject(EngineObject? owner);

    EngineObject? GetOwnerObject();
}

public interface IProperty<T> : IProperty
{
    T DefaultValue { get; }

    T CurrentValue { get; set; }

    IAnimation<T>? Animation { get; set; }

    T GetValue(IClock clock);

    event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    void operator <<=(T value);
}

public class PropertyValueChangedEventArgs<T>(IProperty<T> property, T oldValue, T newValue) : EventArgs
{
    public IProperty<T> Property { get; } = property;

    public T OldValue { get; } = oldValue;

    public T NewValue { get; } = newValue;
}
