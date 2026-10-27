using System.Reflection;
using System.Runtime.CompilerServices;

namespace Beutl;

// The IProperty members of a type, which ScanProperties<T>() registers in the order the type declares them.
public abstract partial class CoreObject : IChildObjectSource
{
    // The members ScanPropertiesCore reads, found once per type rather than for each object.
    private static readonly ConditionalWeakTable<Type, ScannedMember[]> s_scannedMembers = [];
    private List<IProperty>? _properties;

    public virtual IReadOnlyList<IProperty> Properties => RegisteredProperties;

    /// <summary>Raised once when a property is added to this object's registry.</summary>
    /// <remarks>Dynamic properties must have their name, attributes and owner set before registration.</remarks>
    public event EventHandler<IProperty>? PropertyRegistered;

    // The properties registered here, whatever a derived type shows as Properties.
    internal IReadOnlyList<IProperty> RegisteredProperties => (IReadOnlyList<IProperty>?)_properties ?? [];

    // ChildObjects goes on from each property to its value and keyframes.
    IEnumerable<object?> IChildObjectSource.GetChildObjects() => Properties;

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : CoreObject
    {
        var type = typeof(T);
        ScannedMember[] members = s_scannedMembers.GetValue(type, static type =>
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(info => typeof(IProperty).IsAssignableFrom(info.PropertyType))
                .Select(info => new ScannedMember(
                    info.Name,
                    PropertyReflectionCache.GetOrCreateAccessor(type, info),
                    PropertyReflectionCache.GetOrCreateAttributes(type, info.Name, () => [.. info.GetCustomAttributes()]))),
        ]);

        foreach (ScannedMember member in members)
        {
            var property = member.Accessor(this);
            if (property == null) continue;

            member.Validator ??= PropertyReflectionCache.GetOrCreateValidator(type, member.Name,
                () => property.CreateValidator(member.Attributes));

            property.SetAttributes(member.Name, member.Attributes);
            property.SetValidator(member.Validator);
            property.SetOwnerObject(this);
            yield return property;
        }
    }

    // The validator is made from the first object's property: every object of the type gets the same one.
    private sealed class ScannedMember(string name, Func<object, IProperty?> accessor, Attribute[] attributes)
    {
        public string Name { get; } = name;

        public Func<object, IProperty?> Accessor { get; } = accessor;

        public Attribute[] Attributes { get; } = attributes;

        public Validation.IValidator? Validator { get; set; }
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
            PropertyRegistered?.Invoke(this, property);
        }
    }

    /// <summary>Whether <paramref name="property"/> is saved with this object.</summary>
    /// <remarks>
    /// <see cref="NotAutoSerializedAttribute"/> marks state outside the document, such as an editor's, so the value
    /// is neither written nor read back.
    /// </remarks>
    internal static bool IsAutoSerialized(IProperty property)
        => property.GetAttributes()?.Any(static attribute => attribute is NotAutoSerializedAttribute) != true;

    // A base field is saved before Id and Name; an object without panels has each value-only property as one.
    internal virtual bool IsBaseField(IProperty property) => property is not (IExpressionProperty or IListProperty);

    // Id and Name, which come after the base fields when saved and shown.
    internal bool IsOwnField(IProperty property) => ReferenceEquals(property, Id) || ReferenceEquals(property, Name);

    private void RegisterOwnField(IProperty property, string name)
    {
        property.SetAttributes(name, []);
        property.SetOwnerObject(this);
        RegisterProperty(property);
    }
}
