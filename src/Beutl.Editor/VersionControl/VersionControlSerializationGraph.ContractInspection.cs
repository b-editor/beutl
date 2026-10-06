using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Beutl.IO;
using Beutl.Serialization;

namespace Beutl.Editor;

internal static partial class VersionControlSerializationGraph
{
    private sealed partial class SerializationGraphVisitor
    {
        private void InspectRoundTrippedValue(
            JsonNode? node,
            Type declaredType,
            JsonSerializerOptions options,
            bool rootFileSourceIsAddressable,
            bool validateStableRoundTrip = false,
            Uri? baseUri = null,
            string? contractName = null,
            Type? serializedType = null)
        {
            if (node is null)
            {
                return;
            }

            SerializedContractInspection inspection = validateStableRoundTrip
                ? InspectSerializedContract(
                    node,
                    serializedType ?? declaredType,
                    options,
                    baseUri,
                    rootFileSourceIsAddressable,
                    contractName ?? declaredType.FullName ?? declaredType.Name)
                : default;
            object? restored = JsonSerializer.Deserialize(node, declaredType, options);
            if (restored is not null
                && MayContainExternalResource(declaredType)
                && IsOpaqueJsonContract(declaredType, restored.GetType(), options)
                && ContainsUnavailableFileSource(restored, []))
            {
                throw new InvalidDataException(
                    $"Serialized node '{contractName ?? declaredType.FullName}' "
                    + "contains a file source whose URI cannot be recovered safely.");
            }

            if (validateStableRoundTrip)
            {
                ValidateStableJsonRoundTrip(
                    node,
                    restored,
                    declaredType,
                    options,
                    contractName ?? declaredType.FullName ?? declaredType.Name,
                    inspection.CapturedFileSource && inspection.IsComplete);
            }
            var visited = new Dictionary<object, HashSet<RoundTripVisitKey>>(
                ReferenceEqualityComparer.Instance);
            ScanRoundTrippedResources(
                restored,
                new ScanContract(declaredType, options),
                rootFileSourceIsAddressable,
                visited,
                opaqueAncestor: false);
        }

        private static bool ContainsUnavailableFileSource(
            object? value,
            HashSet<object> visited)
        {
            if (value is null or string or JsonNode or JsonElement or JsonDocument)
            {
                return false;
            }

            if (value is IFileSource fileSource)
            {
                try
                {
                    _ = fileSource.Uri;
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }

            Type type = value.GetType();
            if (!MayContainExternalResource(type))
            {
                return false;
            }

            if (!type.IsValueType && !visited.Add(value))
            {
                return false;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (ContainsUnavailableFileSource(item, visited))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (FieldInfo field in GetInstanceFields(type))
            {
                if (ContainsUnavailableFileSource(field.GetValue(value), visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateStableJsonRoundTrip(
            JsonNode node,
            object? restored,
            Type declaredType,
            JsonSerializerOptions options,
            string contractName,
            bool allowUnavailableFileSource)
        {
            JsonNode? roundTripped;
            try
            {
                roundTripped = JsonSerializer.SerializeToNode(
                    restored,
                    declaredType,
                    options);
            }
            catch (InvalidOperationException ex) when (
                allowUnavailableFileSource
                && ex.TargetSite is { Name: "get_Uri", DeclaringType: { } declaringType }
                && declaringType == typeof(BlobFileSource))
            {
                return;
            }

            if (!JsonNode.DeepEquals(node, roundTripped))
            {
                throw new InvalidDataException(
                    $"Serialized node '{contractName}' contains data outside its typed contract.");
            }
        }

        private SerializedContractInspection InspectSerializedContract(
            JsonNode? node,
            Type contractType,
            JsonSerializerOptions options,
            Uri? baseUri,
            bool fileSourceIsAddressable,
            string contractName)
        {
            if (node is null)
            {
                return SerializedContractInspection.Complete;
            }

            contractType = Nullable.GetUnderlyingType(contractType) ?? contractType;
            if (typeof(IFileSource).IsAssignableFrom(contractType))
            {
                return InspectFileSourceNode(
                    node,
                    baseUri,
                    fileSourceIsAddressable,
                    contractName);
            }

            if (contractType == typeof(FileInfo) || contractType == typeof(DirectoryInfo))
            {
                return InspectFileSystemInfoNode(node, baseUri, contractName);
            }

            if (IsRawJsonCarrier(contractType))
            {
                throw new InvalidDataException(
                    $"Cannot safely inspect raw serialized node '{contractName}' for external resources.");
            }

            JsonTypeInfo contractTypeInfo = options.GetTypeInfo(contractType);
            if (contractTypeInfo.Kind == JsonTypeInfoKind.None)
            {
                return InspectScalarContract(
                    node,
                    contractType,
                    contractTypeInfo,
                    options,
                    baseUri,
                    contractName);
            }

            if (node is JsonArray array)
            {
                Type? elementType = ArrayTypeHelpers.GetElementType(contractType);
                if (elementType is null)
                {
                    return default;
                }

                return InspectElements(
                    array,
                    elementType,
                    options,
                    baseUri,
                    contractName);
            }

            if (node is not JsonObject jsonObject)
            {
                return SerializedContractInspection.Complete;
            }

            if (typeof(IDictionary).IsAssignableFrom(contractType)
                && ArrayTypeHelpers.GetEntryType(contractType)
                    is (Type keyType, Type valueType)
                && keyType == typeof(string))
            {
                return InspectElements(
                    jsonObject.Select(static entry => entry.Value),
                    valueType,
                    options,
                    baseUri,
                    contractName);
            }

            JsonTypeInfo typeInfo = contractTypeInfo;
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return default;
            }

            string? discriminator = typeInfo.PolymorphismOptions
                ?.TypeDiscriminatorPropertyName;
            string discriminatorName = discriminator ?? "$type";
            typeInfo = ResolvePolymorphicTypeInfo(
                typeInfo,
                jsonObject,
                discriminatorName,
                options);
            return InspectObjectMembers(
                jsonObject,
                typeInfo,
                discriminatorName,
                options,
                baseUri,
                contractName);
        }

        private SerializedContractInspection InspectFileSourceNode(
            JsonNode node,
            Uri? baseUri,
            bool fileSourceIsAddressable,
            string contractName)
        {
            if (node is not JsonValue value
                || !value.TryGetValue(out string? uriString)
                || !Uri.TryCreate(
                    uriString,
                    UriKind.RelativeOrAbsolute,
                    out Uri? uri))
            {
                throw new InvalidDataException(
                    $"Serialized file source '{contractName}' does not contain a valid URI.");
            }

            if (!uri.IsAbsoluteUri)
            {
                if (baseUri is null || !Uri.TryCreate(baseUri, uri, out uri))
                {
                    throw new InvalidDataException(
                        $"Serialized file source '{contractName}' has an unresolved relative URI.");
                }
            }

            if (fileSourceIsAddressable)
            {
                _addressableFileSources.Add(uri);
            }
            else
            {
                _unaddressableFileSources.Add(uri);
            }

            return new SerializedContractInspection(
                CapturedFileSource: true,
                IsComplete: true);
        }

        private SerializedContractInspection InspectFileSystemInfoNode(
            JsonNode node,
            Uri? baseUri,
            string contractName)
        {
            if (node is not JsonValue value
                || !value.TryGetValue(out string? path)
                || !TryResolveOpaqueFileUri(
                    path,
                    baseUri,
                    allowExtensionlessRelative: true,
                    requireFilePath: true,
                    out Uri? uri))
            {
                throw new InvalidDataException(
                    $"Serialized file-system path '{contractName}' cannot be resolved.");
            }

            _unaddressableFileSources.Add(uri);
            return new SerializedContractInspection(
                CapturedFileSource: true,
                IsComplete: true);
        }

        // A known resource-free scalar must round-trip unchanged. Any other scalar is incomplete; its JSON is searched
        // for file URIs unless a built-in converter handles a type that holds no resources.
        private SerializedContractInspection InspectScalarContract(
            JsonNode node,
            Type contractType,
            JsonTypeInfo contractTypeInfo,
            JsonSerializerOptions options,
            Uri? baseUri,
            string contractName)
        {
            if (!IsKnownResourceFreeScalarContract(contractType, contractTypeInfo))
            {
                if (!IsSystemTextJsonConverter(contractTypeInfo.Converter)
                    || MayContainExternalResource(contractType))
                {
                    CaptureOpaqueFileUris(node, baseUri);
                }

                return default;
            }

            object? restoredScalar = JsonSerializer.Deserialize(node, contractType, options);
            JsonNode? roundTrippedScalar = JsonSerializer.SerializeToNode(
                restoredScalar,
                contractType,
                options);
            if (!JsonNode.DeepEquals(node, roundTrippedScalar))
            {
                throw new InvalidDataException(
                    $"Serialized scalar '{contractName}' is not stable under its typed contract.");
            }

            return SerializedContractInspection.Complete;
        }

        private SerializedContractInspection InspectElements(
            IEnumerable<JsonNode?> items,
            Type elementType,
            JsonSerializerOptions options,
            Uri? baseUri,
            string contractName)
        {
            SerializedContractInspection inspection = SerializedContractInspection.Complete;
            foreach (JsonNode? item in items)
            {
                inspection = inspection.Combine(InspectSerializedContract(
                    item,
                    elementType,
                    options,
                    baseUri,
                    fileSourceIsAddressable: false,
                    contractName));
            }

            return inspection;
        }

        // The derived contract named by the object's discriminator; the declared contract when none matches.
        private static JsonTypeInfo ResolvePolymorphicTypeInfo(
            JsonTypeInfo typeInfo,
            JsonObject jsonObject,
            string discriminatorName,
            JsonSerializerOptions options)
        {
            if (typeInfo.PolymorphismOptions is { } polymorphism
                && jsonObject[discriminatorName] is JsonValue discriminatorValue)
            {
                Type? derivedContractType = null;
                foreach (JsonDerivedType candidate in polymorphism.DerivedTypes)
                {
                    bool matches = candidate.TypeDiscriminator switch
                    {
                        string text => discriminatorValue.TryGetValue(out string? stringValue)
                                       && string.Equals(stringValue, text, StringComparison.Ordinal),
                        int number => discriminatorValue.TryGetValue(out int intValue)
                                      && intValue == number,
                        _ => false,
                    };
                    if (matches)
                    {
                        derivedContractType = candidate.DerivedType;
                        break;
                    }
                }

                if (derivedContractType is not null)
                {
                    typeInfo = options.GetTypeInfo(derivedContractType);
                }
            }

            return typeInfo;
        }

        private SerializedContractInspection InspectObjectMembers(
            JsonObject jsonObject,
            JsonTypeInfo typeInfo,
            string discriminatorName,
            JsonSerializerOptions options,
            Uri? baseUri,
            string contractName)
        {
            StringComparer comparer = options.PropertyNameCaseInsensitive
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            Dictionary<string, JsonPropertyInfo> properties = typeInfo.Properties
                .ToDictionary(property => property.Name, comparer);
            SerializedContractInspection result = SerializedContractInspection.Complete;
            foreach ((string name, JsonNode? item) in jsonObject)
            {
                if (!properties.TryGetValue(name, out JsonPropertyInfo? property))
                {
                    bool isDiscriminator = name is "$type" or "@type"
                                           || string.Equals(
                                               name,
                                               discriminatorName,
                                               StringComparison.Ordinal);
                    if (isDiscriminator && item is JsonValue)
                    {
                        continue;
                    }

                    throw new InvalidDataException(
                        $"Serialized node '{contractName}' contains unknown member '{name}'.");
                }

                if (property.CustomConverter is not null
                    && !typeof(IFileSource).IsAssignableFrom(property.PropertyType))
                {
                    if (!IsSystemTextJsonConverter(property.CustomConverter)
                        || MayContainExternalResource(property.PropertyType))
                    {
                        CaptureOpaqueFileUris(item, baseUri);
                    }

                    result = result.Combine(default);
                }
                else
                {
                    result = result.Combine(InspectSerializedContract(
                        item,
                        property.PropertyType,
                        options,
                        baseUri,
                        fileSourceIsAddressable: false,
                        contractName));
                }
            }

            return result;
        }

        private readonly record struct SerializedContractInspection(
            bool CapturedFileSource,
            bool IsComplete)
        {
            public static SerializedContractInspection Complete => new(false, true);

            public SerializedContractInspection Combine(SerializedContractInspection other)
            {
                return new SerializedContractInspection(
                    CapturedFileSource || other.CapturedFileSource,
                    IsComplete && other.IsComplete);
            }
        }
    }
}
