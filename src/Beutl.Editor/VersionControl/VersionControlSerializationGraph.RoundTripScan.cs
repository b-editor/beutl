using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beutl.Graphics;
using Beutl.IO;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Editor;

internal static partial class VersionControlSerializationGraph
{
    private sealed partial class SerializationGraphVisitor
    {
        private readonly record struct ScanContract(
            Type DeclaredType,
            JsonSerializerOptions Options,
            bool ExplicitOpaque = false);

        private readonly record struct RoundTripVisitKey(
            Type DeclaredType,
            JsonSerializerOptions Options,
            bool OpaquePath);

        private void ScanRoundTrippedResources(
            object? value,
            ScanContract contract,
            bool fileSourceIsAddressable,
            Dictionary<object, HashSet<RoundTripVisitKey>> visited,
            bool opaqueAncestor)
        {
            switch (value)
            {
                case null or string:
                    return;
                case JsonNode or JsonElement or JsonDocument:
                    throw new InvalidDataException(
                        "Cannot safely inspect raw serialized JSON for external resources.");
                case IFileSource fileSource:
                    RecordFileSource(fileSource, fileSourceIsAddressable);
                    return;
                case FontFamily or Typeface:
                    return;
            }

            Type type = value.GetType();
            bool opaqueContract = contract.ExplicitOpaque
                                  || IsOpaqueJsonContract(
                                      contract.DeclaredType,
                                      type,
                                      contract.Options);
            bool opaquePath = opaqueAncestor || opaqueContract;
            if (!type.IsValueType
                && !TryEnterRoundTripVisit(value, contract, opaquePath, visited))
            {
                return;
            }

            if (value is IOptional optional)
            {
                if (optional.HasValue)
                {
                    ScanRoundTrippedResources(
                        optional.ToObject().Value,
                        new ScanContract(optional.GetValueType(), contract.Options),
                        fileSourceIsAddressable: false,
                        visited,
                        opaquePath);
                }

                return;
            }

            if (value is ICoreSerializable serializable)
            {
                if (opaquePath)
                {
                    List<FieldInfo> coreObjectFields = GetInstanceFields(type);
                    ValidateOpaqueResourceAccessors(type, coreObjectFields);
                    Dictionary<FieldInfo, ScanContract> coreObjectFieldContracts
                        = GetFieldContracts(type, contract.Options);
                    ScanOpaqueFields(
                        value,
                        coreObjectFields,
                        coreObjectFieldContracts,
                        contract.Options,
                        visited,
                        opaquePath);
                }

                VisitCoreSerializable(serializable);
                return;
            }

            if (value is IReference)
            {
                return;
            }

            if (value is IDictionary dictionary)
            {
                Type valueType = GetDictionaryValueType(contract.DeclaredType)
                                 ?? GetDictionaryValueType(type)
                                 ?? typeof(object);
                foreach (object? item in dictionary.Values)
                {
                    ScanRoundTrippedResources(
                        item,
                        new ScanContract(valueType, contract.Options),
                        fileSourceIsAddressable: false,
                        visited,
                        opaquePath);
                }

                if (opaqueContract
                    || (opaquePath && !IsFrameworkCollectionType(type)))
                {
                    throw new InvalidDataException(
                        $"Cannot safely inspect opaque dictionary contract '{type.FullName}'.");
                }

                if (opaquePath)
                {
                    Type keyType = GetDictionaryKeyType(contract.DeclaredType)
                                   ?? GetDictionaryKeyType(type)
                                   ?? typeof(object);
                    foreach (object? key in dictionary.Keys)
                    {
                        ScanRoundTrippedResources(
                            key,
                            new ScanContract(keyType, contract.Options),
                            fileSourceIsAddressable: false,
                            visited,
                            opaquePath);
                    }

                    // Inspect the restored storage, including wrapped dictionaries and comparers.
                    ScanOpaqueFields(
                        value,
                        GetInstanceFields(type),
                        GetFieldContracts(type, contract.Options),
                        contract.Options,
                        visited,
                        opaquePath);
                }

                return;
            }
            else if (value is IEnumerable enumerable)
            {
                Type elementType = ArrayTypeHelpers.GetElementType(contract.DeclaredType)
                                   ?? ArrayTypeHelpers.GetElementType(type)
                                   ?? typeof(object);
                foreach (object? item in enumerable)
                {
                    ScanRoundTrippedResources(
                        item,
                        new ScanContract(elementType, contract.Options),
                        fileSourceIsAddressable: false,
                        visited,
                        opaquePath);
                }

                if (opaqueContract
                    || (opaquePath && !IsFrameworkCollectionType(type)))
                {
                    throw new InvalidDataException(
                        $"Cannot safely inspect opaque collection contract '{type.FullName}'.");
                }

                if (opaquePath && !type.IsArray)
                {
                    // A standard collection below a custom converter is still inspectable. Scan its
                    // storage too, so a read-only view cannot hide an opaque custom backing list.
                    ScanOpaqueFields(
                        value,
                        GetInstanceFields(type),
                        GetFieldContracts(type, contract.Options),
                        contract.Options,
                        visited,
                        opaquePath);
                }

                return;
            }

            if (type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(Memory<>)
                    || type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>))
                && type.GetMethod("ToArray", BindingFlags.Instance | BindingFlags.Public)
                    ?.Invoke(value, null) is IEnumerable memoryItems)
            {
                foreach (object? item in memoryItems)
                {
                    ScanRoundTrippedResources(
                        item,
                        new ScanContract(type.GetGenericArguments()[0], contract.Options),
                        fileSourceIsAddressable: false,
                        visited,
                        opaquePath);
                }

                return;
            }

            if (type.Assembly == typeof(object).Assembly
                && !MayContainExternalResource(contract.DeclaredType)
                && !MayContainExternalResource(type))
            {
                return;
            }

            List<FieldInfo> fields = GetInstanceFields(type);
            Dictionary<FieldInfo, ScanContract> fieldContracts
                = GetFieldContracts(type, contract.Options);

            if (opaquePath)
            {
                ValidateOpaqueResourceAccessors(type, fields);
                ScanOpaqueFields(
                    value,
                    fields,
                    fieldContracts,
                    contract.Options,
                    visited,
                    opaquePath);
            }
            else
            {
                foreach ((FieldInfo field, ScanContract fieldContract) in fieldContracts)
                {
                    ScanRoundTrippedResources(
                        field.GetValue(value),
                        fieldContract,
                        fileSourceIsAddressable: false,
                        visited,
                        opaquePath);
                }
            }
        }

        private static bool IsFrameworkCollectionType(Type type)
        {
            // Framework collections span CoreLib and System.Collections. Compare the actual
            // framework assemblies so extension subclasses cannot inherit this exemption.
            return type.IsArray
                   || type.Assembly == typeof(List<>).Assembly
                   || type.Assembly == typeof(SortedSet<>).Assembly;
        }

        // An opaque value is scanned field by field, each under its JSON contract when it has one.
        private void ScanOpaqueFields(
            object value,
            List<FieldInfo> fields,
            Dictionary<FieldInfo, ScanContract> fieldContracts,
            JsonSerializerOptions options,
            Dictionary<object, HashSet<RoundTripVisitKey>> visited,
            bool opaquePath)
        {
            foreach (FieldInfo field in fields)
            {
                ScanContract fieldContract = fieldContracts.GetValueOrDefault(
                    field,
                    new ScanContract(field.FieldType, options));
                ScanRoundTrippedResources(
                    field.GetValue(value),
                    fieldContract,
                    fileSourceIsAddressable: false,
                    visited,
                    opaquePath);
            }
        }

        private static bool TryEnterRoundTripVisit(
            object value,
            ScanContract contract,
            bool opaquePath,
            Dictionary<object, HashSet<RoundTripVisitKey>> visited)
        {
            if (!visited.TryGetValue(value, out HashSet<RoundTripVisitKey>? contracts))
            {
                contracts = [];
                visited.Add(value, contracts);
            }

            return contracts.Add(new RoundTripVisitKey(
                contract.DeclaredType,
                contract.Options,
                opaquePath));
        }

        private static bool IsOpaqueJsonContract(
            Type declaredType,
            Type runtimeType,
            JsonSerializerOptions options)
        {
            return options.GetTypeInfo(declaredType).Kind == JsonTypeInfoKind.None
                   || (runtimeType != declaredType
                       && options.GetTypeInfo(runtimeType).Kind == JsonTypeInfoKind.None);
        }

        private static List<FieldInfo> GetInstanceFields(Type type)
        {
            List<FieldInfo> fields = [];
            for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                fields.AddRange(current.GetFields(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly)
                    .Where(field => !IsJsonIgnoredField(field)));
            }

            return fields;
        }

        private static bool IsJsonIgnoredField(FieldInfo field)
        {
            if (IsAlwaysJsonIgnored(field))
            {
                return true;
            }

            const string BackingFieldSuffix = ">k__BackingField";
            if (!field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                || field.Name.Length <= BackingFieldSuffix.Length + 1
                || field.Name[0] != '<'
                || !field.Name.EndsWith(BackingFieldSuffix, StringComparison.Ordinal))
            {
                return false;
            }

            string propertyName = field.Name[1..^BackingFieldSuffix.Length];
            PropertyInfo? property = field.DeclaringType?.GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
            return property is not null && IsAlwaysJsonIgnored(property);
        }

        private static bool IsAlwaysJsonIgnored(MemberInfo member)
        {
            return member.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true)?.Condition
                   == JsonIgnoreCondition.Always;
        }

        private static Dictionary<FieldInfo, ScanContract> GetFieldContracts(
            Type runtimeType,
            JsonSerializerOptions options)
        {
            var result = new Dictionary<FieldInfo, ScanContract>();
            JsonTypeInfo typeInfo = options.GetTypeInfo(runtimeType);
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return result;
            }

            foreach (JsonPropertyInfo jsonProperty in typeInfo.Properties)
            {
                FieldInfo? field = jsonProperty.AttributeProvider switch
                {
                    FieldInfo fieldInfo => fieldInfo,
                    PropertyInfo propertyInfo => TryGetTrivialPropertyBackingField(propertyInfo),
                    _ => null,
                };
                bool explicitOpaque = jsonProperty.CustomConverter is not null
                                      || jsonProperty.AttributeProvider
                                          ?.GetCustomAttributes(
                                              typeof(JsonConverterAttribute),
                                              inherit: true)
                                          .Length > 0;

                if (field is not null)
                {
                    result[field] = new ScanContract(
                        jsonProperty.PropertyType,
                        options,
                        explicitOpaque);
                }
                else if (jsonProperty.Get is not null
                         && MayContainExternalResource(jsonProperty.PropertyType))
                {
                    throw new InvalidDataException(
                        $"Cannot safely inspect serialized resource property "
                        + $"'{runtimeType.FullName}.{jsonProperty.Name}' without invoking its getter.");
                }
            }

            return result;
        }

        private static void ValidateOpaqueResourceAccessors(
            Type type,
            IReadOnlyCollection<FieldInfo> fields)
        {
            foreach (PropertyInfo property in GetOpaqueResourceProperties(type))
            {
                if (IsAlwaysJsonIgnored(property)
                    || property.GetMethod is null
                    || property.GetIndexParameters().Length != 0
                    || !MayContainExternalResource(property.PropertyType)
                    || IsSynthesizedRecordEqualityContract(property))
                {
                    continue;
                }

                FieldInfo? backingField = TryGetTrivialPropertyBackingField(property);
                if (backingField is null || !fields.Contains(backingField))
                {
                    throw new InvalidDataException(
                        $"Cannot safely inspect external-resource accessor "
                        + $"'{type.FullName}.{property.Name}' without invoking its getter.");
                }
            }
        }

        private static bool IsSynthesizedRecordEqualityContract(PropertyInfo property)
        {
            // Records synthesize EqualityContract as `typeof(TRecord)`: no backing field, but no
            // resource either. Verify that body in the IL rather than trusting [CompilerGenerated],
            // which any getter can carry; treating the abstract System.Type as resource-free would
            // also trust hand-written Type members.
            if (property.Name != "EqualityContract" || property.PropertyType != typeof(Type))
            {
                return false;
            }

            MethodInfo? getter = property.GetMethod;
            byte[]? il = getter?.GetMethodBody()?.GetILAsByteArray();
            if (il is not { Length: 11 }
                || il[0] != 0xd0 // ldtoken
                || il[5] != 0x28 // call
                || il[10] != 0x2a) // ret
            {
                return false;
            }

            try
            {
                Type[]? typeArguments = getter!.DeclaringType?.GetGenericArguments();
                Type[] methodArguments = getter.GetGenericArguments();
                Type loadedType = getter.Module.ResolveType(
                    BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(1, 4)),
                    typeArguments,
                    methodArguments);
                MethodBase? calledMethod = getter.Module.ResolveMethod(
                    BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(6, 4)),
                    typeArguments,
                    methodArguments);
                return loadedType == property.DeclaringType
                       && calledMethod is { Name: nameof(Type.GetTypeFromHandle) }
                       && calledMethod.DeclaringType == typeof(Type);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static IEnumerable<PropertyInfo> GetOpaqueResourceProperties(Type type)
        {
            HashSet<PropertyInfo> yielded = [];
            foreach (PropertyInfo property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                if (yielded.Add(property))
                {
                    yield return property;
                }
            }

            Type? nonPublicStop = typeof(CoreObject).IsAssignableFrom(type)
                ? typeof(CoreObject)
                : typeof(object);
            for (Type? current = type;
                 current is not null && current != nonPublicStop;
                 current = current.BaseType)
            {
                foreach (PropertyInfo property in current.GetProperties(
                             BindingFlags.Instance
                             | BindingFlags.NonPublic
                             | BindingFlags.DeclaredOnly))
                {
                    if (yielded.Add(property))
                    {
                        yield return property;
                    }
                }
            }
        }

        private static FieldInfo? TryGetTrivialPropertyBackingField(PropertyInfo property)
        {
            MethodInfo? getter = property.GetMethod;
            FieldInfo? field = property.DeclaringType?.GetField(
                $"<{property.Name}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (getter?.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) == true
                && field?.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) == true
                && field.FieldType == property.PropertyType)
            {
                return field;
            }

            byte[]? il = getter?.GetMethodBody()?.GetILAsByteArray();
            if (il is not { Length: 7 }
                || il[0] != 0x02 // ldarg.0
                || il[1] != 0x7b // ldfld
                || il[6] != 0x2a) // ret
            {
                return null;
            }

            try
            {
                int token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(2, 4));
                FieldInfo? resolved = getter!.Module.ResolveField(
                    token,
                    getter.DeclaringType?.GetGenericArguments(),
                    getter.GetGenericArguments());
                return resolved?.FieldType == property.PropertyType ? resolved : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static Type? GetDictionaryKeyType(Type type)
        {
            return ArrayTypeHelpers.GetEntryType(type) is (Type keyType, _)
                ? keyType
                : null;
        }

        private static Type? GetDictionaryValueType(Type type)
        {
            return ArrayTypeHelpers.GetEntryType(type) is (_, Type valueType)
                ? valueType
                : null;
        }

        private static bool IsRawJsonCarrier(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return typeof(JsonNode).IsAssignableFrom(type)
                   || type == typeof(JsonElement)
                   || type == typeof(JsonDocument);
        }

        private static bool IsKnownResourceFreeScalarContract(
            Type type,
            JsonTypeInfo typeInfo)
        {
            if (IsKnownResourceFreeScalarType(type)
                && IsSystemTextJsonConverter(typeInfo.Converter))
            {
                return true;
            }

            Assembly typeAssembly = type.Assembly;
            return !MayContainExternalResource(type)
                   && typeInfo.Converter.GetType().Assembly == typeAssembly
                   && (typeAssembly == typeof(Rational).Assembly
                       || typeAssembly == typeof(Point).Assembly);
        }

        private static bool IsSystemTextJsonConverter(JsonConverter converter)
        {
            return converter.GetType().Assembly == typeof(JsonSerializer).Assembly;
        }

        private static bool IsKnownResourceFreeScalarType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(string)
                   || type == typeof(Uri)
                   || type == typeof(Guid)
                   || type == typeof(DateTime)
                   || type == typeof(DateTimeOffset)
                   || type == typeof(TimeSpan)
                   || type == typeof(decimal)
                   || type.IsPrimitive
                   || type.IsEnum;
        }

        private static bool MayContainExternalResource(Type type)
        {
            return MayContainExternalResource(type, []);
        }

        private static bool MayContainExternalResource(Type type, HashSet<Type> visited)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (typeof(IFileSource).IsAssignableFrom(type)
                || typeof(ICoreSerializable).IsAssignableFrom(type)
                || typeof(IOptional).IsAssignableFrom(type)
                || type == typeof(FontFamily)
                || type == typeof(Typeface)
                || type == typeof(object)
                || IsRawJsonCarrier(type))
            {
                return true;
            }

            if (IsKnownResourceFreeScalarType(type))
            {
                return false;
            }

            if (type.IsArray)
            {
                return MayContainExternalResource(type.GetElementType()!, visited);
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                Type? valueType = GetDictionaryValueType(type);
                return valueType is null
                       || MayContainExternalResource(valueType, visited);
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                Type? elementType = ArrayTypeHelpers.GetElementType(type);
                return elementType is null
                       || MayContainExternalResource(elementType, visited);
            }

            if (type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(Memory<>)
                    || type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>)))
            {
                return MayContainExternalResource(type.GetGenericArguments()[0], visited);
            }

            if (type.IsInterface || type.IsAbstract)
            {
                return true;
            }

            if (type.IsGenericType
                && type.GetGenericArguments().Any(argument =>
                    MayContainExternalResource(argument, visited)))
            {
                return true;
            }

            if (!type.IsValueType && !type.IsSealed)
            {
                return true;
            }

            if (!visited.Add(type) || type.Assembly == typeof(object).Assembly)
            {
                return false;
            }

            try
            {
                return type.GetProperties(
                               BindingFlags.Instance
                               | BindingFlags.Public
                               | BindingFlags.NonPublic)
                           .Where(property => property.GetIndexParameters().Length == 0)
                           .Any(property => MayContainExternalResource(property.PropertyType, visited))
                       || type.GetFields(
                               BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                           .Any(field => MayContainExternalResource(field.FieldType, visited));
            }
            finally
            {
                visited.Remove(type);
            }
        }
    }
}
