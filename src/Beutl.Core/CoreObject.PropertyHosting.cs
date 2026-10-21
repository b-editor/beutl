using System.Reflection;
using System.Runtime.CompilerServices;

namespace Beutl;

// The IProperty members of a type, which ScanProperties<T>() registers in the order the type declares them.
public abstract partial class CoreObject : IChildObjectSource
{
    // The members ScanPropertiesCore reads and their names, found once per type rather than for each object.
    private static readonly ConditionalWeakTable<Type, (PropertyInfo Info, string Name)[]> s_scannedMembers = [];
    private List<IProperty>? _properties;

    public virtual IReadOnlyList<IProperty> Properties => RegisteredProperties;

    // The properties registered here, whatever a derived type shows as Properties.
    internal IReadOnlyList<IProperty> RegisteredProperties => (IReadOnlyList<IProperty>?)_properties ?? [];

    // ChildObjects goes on from each property to its value and keyframes.
    IEnumerable<object?> IChildObjectSource.GetChildObjects() => Properties;

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : CoreObject
    {
        var type = typeof(T);
        (PropertyInfo Info, string Name)[] members = s_scannedMembers.GetValue(type, static type =>
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(info => typeof(IProperty).IsAssignableFrom(info.PropertyType))
                .Select(info => (info, ToPropertyName(info.Name))),
        ]);

        foreach ((PropertyInfo propertyInfo, string name) in members)
        {
            var func = PropertyReflectionCache.GetOrCreateAccessor(type, propertyInfo);
            var property = func(this);
            if (property == null) continue;

            var attrs = PropertyReflectionCache.GetOrCreateAttributes(type, propertyInfo.Name,
                () => [.. propertyInfo.GetCustomAttributes()]);
            var validator = PropertyReflectionCache.GetOrCreateValidator(type, propertyInfo.Name,
                () => property.CreateValidator(attrs));

            property.SetAttributes(name, attrs);
            property.SetValidator(validator);
            property.SetOwnerObject(this);
            yield return property;
        }
    }

    // A property exposed as XxxProperty, beside a value accessor Xxx, is registered, saved and recorded as Xxx.
    private static string ToPropertyName(string memberName)
        => memberName.Length > "Property".Length && memberName.EndsWith("Property", StringComparison.Ordinal)
            ? memberName[..^"Property".Length]
            : memberName;

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

    /// <summary>Whether <paramref name="property"/> is saved with this object.</summary>
    /// <remarks>
    /// <see cref="NotAutoSerializedAttribute"/> means here what it means on a <see cref="CoreProperty"/>: the value
    /// is state outside the document, such as an editor's, so it is neither written nor read back.
    /// </remarks>
    internal static bool IsAutoSerialized(IProperty property)
        => property.GetAttributes()?.Any(static attribute => attribute is NotAutoSerializedAttribute) != true;

    // A base field stands for a CoreProperty field; an object without panels has each value-only property as one.
    internal virtual bool IsBaseField(IProperty property) => property is not (IExpressionProperty or IListProperty);

    // Id and Name, which come after the CoreProperty fields when saved and shown, as their CoreProperty fields did.
    internal bool IsOwnField(IProperty property) => property == IdProperty || property == NameProperty;

    private void RegisterOwnField(IProperty property, string name)
    {
        property.SetAttributes(name, []);
        property.SetOwnerObject(this);
        RegisterProperty(property);
    }
}
