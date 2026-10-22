using System.Reflection;

namespace Beutl;

// The IProperty members of a type, which ScanProperties<T>() registers in the order the type declares them.
public abstract partial class CoreObject : IChildObjectSource
{
    private List<IProperty>? _properties;

    public virtual IReadOnlyList<IProperty> Properties => RegisteredProperties;

    // The properties registered here, whatever a derived type shows as Properties.
    internal IReadOnlyList<IProperty> RegisteredProperties => (IReadOnlyList<IProperty>?)_properties ?? [];

    // ChildObjects goes on from each property to its value and keyframes.
    IEnumerable<object?> IChildObjectSource.GetChildObjects() => Properties;

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : CoreObject
    {
        var type = typeof(T);
        var propertyInfos = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        for (int index = 0; index < propertyInfos.Length; index++)
        {
            PropertyInfo propertyInfo = propertyInfos[index];
            if (!typeof(IProperty).IsAssignableFrom(propertyInfo.PropertyType)) continue;

            var func = PropertyReflectionCache.GetOrCreateAccessor(type, propertyInfo);
            var property = func(this);
            if (property == null) continue;

            var attrs = PropertyReflectionCache.GetOrCreateAttributes(type, propertyInfo.Name,
                () => [.. propertyInfo.GetCustomAttributes()]);
            var validator = PropertyReflectionCache.GetOrCreateValidator(type, propertyInfo.Name,
                () => property.CreateValidator(attrs));

            property.SetAttributes(propertyInfo.Name, attrs);
            property.SetValidator(validator);
            property.SetOwnerObject(this);
            yield return property;
        }
    }

    protected void ScanProperties<T>() where T : CoreObject
    {
        int index = 0;
        foreach (IProperty property in ScanPropertiesCore<T>())
        {
            RegisterProperty(property, index++);
        }
    }

    protected void RegisterProperty(IProperty property)
    {
        RegisterProperty(property, RegisteredProperties.Count);
    }

    protected virtual void RegisterProperty(IProperty property, int index)
    {
        _properties ??= [];
        if (!_properties.Contains(property))
        {
            _properties.Insert(Math.Min(index, _properties.Count), property);
        }
    }
}
