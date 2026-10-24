using System.Collections;
using System.Reactive;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Beutl.IO;

namespace Beutl.Serialization;

public static partial class CoreSerializer
{
    // Reports each saved value at its member, but expands a shared object only once and never saves its file.
    public static bool Visit(object? root, Func<CoreSerializationVisit, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Walk(root, root?.GetType() ?? typeof(object), null, null, null, null, null);

        bool Walk(object? value, Type type, CoreSerializationVisit? parent, string? member,
            int? index, string? key, Uri? baseUri)
        {
            if (value is IOptional optional)
            {
                return optional.HasValue
                       && Walk(optional.ToObject().Value, optional.GetValueType(), parent, member, index, key, baseUri);
            }

            if (value is null or Unit) return false;
            var visit = new CoreSerializationVisit(value, parent, member, index, key);
            if (visitor(visit)) return true;
            if (value is string or IFileSource or IReference or JsonNode) return false;
            if (!value.GetType().IsValueType && !visited.Add(value)) return false;

            if (value is ICoreSerializable serializable)
            {
                if (value is IFallback { Json: not null } fallback && !fallback.SavesItsMembers()) return false;
                var context = new VisitorContext(value.GetType(), (value as CoreObject)?.Uri ?? baseUri);
                using (ThreadLocalSerializationContext.Enter(context))
                    serializable.Serialize(context);
                foreach ((string name, (object? child, Type childType)) in context.Members)
                {
                    if (Walk(child, childType, visit, name, null, null, context.BaseUri)) return true;
                }

                return false;
            }

            Type actualType = value.GetType();
            Type contractType = type == typeof(object) ? actualType : type;
            if (value is IEnumerable items && !JsonSerializationContext.HasOwnJsonConverter(contractType))
            {
                if (value is IDictionary dictionary
                    && ArrayTypeHelpers.GetEntryType(actualType) is (var keyType, { } valueType)
                    && keyType == typeof(string))
                {
                    if (!ItemsMayHoldObjects(valueType)) return false;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (Walk(entry.Value, valueType, visit, null, null, (string)entry.Key, baseUri)) return true;
                    }
                }
                else
                {
                    Type itemType = ArrayTypeHelpers.GetElementType(actualType) ?? typeof(object);
                    if (!ItemsMayHoldObjects(itemType)) return false;
                    int itemIndex = 0;
                    foreach (object? item in items)
                    {
                        if (Walk(item, itemType, visit, null, itemIndex++, null, baseUri)) return true;
                    }
                }

                return false;
            }

            JsonHelper.SerializerOptions.MakeReadOnly(populateMissingResolver: true);
            JsonTypeInfo contract = JsonHelper.SerializerOptions.GetTypeInfo(contractType);
            if (contract.Kind == JsonTypeInfoKind.Object)
            {
                foreach (JsonPropertyInfo property in contract.Properties)
                {
                    if (property.Get is not { } get) continue;
                    object? child = get(value);
                    if (property.ShouldSerialize?.Invoke(value, child) == false) continue;
                    if (Walk(child, property.PropertyType, visit, property.Name, null, null, baseUri)) return true;
                }
            }

            return false;
        }
    }

    private static bool ItemsMayHoldObjects(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (ChildObjects.MayHoldObjects(type)
            || type.IsAssignableTo(typeof(ICoreSerializable))
            || type.IsAssignableTo(typeof(IFileSource))
            || type.IsAssignableTo(typeof(IReference))) return true;

        JsonHelper.SerializerOptions.MakeReadOnly(populateMissingResolver: true);
        return JsonHelper.SerializerOptions.GetTypeInfo(type).Kind != JsonTypeInfoKind.None;
    }

    internal sealed class VisitorContext(Type ownerType, Uri? baseUri) : ICoreSerializationContext
    {
        public Dictionary<string, (object? Value, Type Type)> Members { get; } = new(StringComparer.Ordinal);

        public CoreSerializationMode Mode => CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects;

        public Uri? BaseUri => baseUri;

        public Type OwnerType => ownerType;

        public void SetValue<T>(string name, T? value)
        {
            if (value is Unit) Members.Remove(name);
            else Members[name] = (value, typeof(T));
        }

        public bool Contains(string name) => Members.ContainsKey(name);

        public T? GetValue<T>(string name) => throw new InvalidOperationException("A serialization visitor cannot read values.");

        public void Populate(string name, ICoreSerializable obj) => throw new InvalidOperationException("A serialization visitor cannot populate objects.");

        public void Resolve(Guid id, Action<ICoreSerializable> callback) => throw new InvalidOperationException("A serialization visitor cannot resolve references.");
    }
}
