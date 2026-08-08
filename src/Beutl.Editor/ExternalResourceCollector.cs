using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.IO;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.Serialization;

namespace Beutl.Editor;

/// <summary>
/// Collects IFileSource references and font references from the project hierarchy.
/// </summary>
public sealed class ExternalResourceCollector
{
    private readonly HashSet<(Guid Object, string PropertyName, Uri OriginalUri)> _fileSources = [];
    private readonly HashSet<FontFamily> _fontFamilies = [];
    private readonly HashSet<Uri> _unaddressableFileSources = [];
    private readonly HashSet<CoreObject> _relocationOwners = new(ReferenceEqualityComparer.Instance);

    private ExternalResourceCollector()
    {
    }

    /// <summary>
    /// The list of collected file sources.
    /// </summary>
    public IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> FileSources => _fileSources;

    /// <summary>
    /// The list of collected font families.
    /// </summary>
    public IEnumerable<FontFamily> FontFamilies => _fontFamilies;

    internal IReadOnlySet<Uri> UnaddressableFileSources => _unaddressableFileSources;

    internal IReadOnlySet<CoreObject> RelocationOwners => _relocationOwners;

    /// <summary>
    /// Collects all resource references within the hierarchy.
    /// </summary>
    /// <param name="root">The root hierarchy to start collecting from.</param>
    /// <param name="projectDirectory">The path of the project directory.</param>
    /// <returns>The collected resource information.</returns>
    public static ExternalResourceCollector Collect(IHierarchical root, string projectDirectory)
    {
        return Collect(root, projectDirectory, stagedStorageObjects: null);
    }

    internal static ExternalResourceCollector Collect(
        IHierarchical root,
        string projectDirectory,
        IReadOnlySet<CoreObject>? stagedStorageObjects)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        return Collect(DiscoverSerializationGraph(root), projectDirectory, stagedStorageObjects);
    }

    internal static ExternalResourceCollector Collect(
        SerializationGraph graph,
        string projectDirectory,
        IReadOnlySet<CoreObject>? stagedStorageObjects)
    {
        ExternalResourceCollector collector = new();
        foreach (CoreObject obj in graph.Objects)
        {
            collector.CollectFromObject(obj, projectDirectory, stagedStorageObjects);
        }

        collector._fontFamilies.UnionWith(graph.FontFamilies);
        collector._unaddressableFileSources.UnionWith(
            graph.UnaddressableFileSources.Where(uri => ShouldRelocateFile(uri, projectDirectory)));

        return collector;
    }

    internal static SerializationGraph DiscoverSerializationGraph(IHierarchical root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var visitor = new SerializationGraphVisitor();
        visitor.Visit(root);
        return new SerializationGraph(
            visitor.Objects,
            visitor.UnaddressableFileSources,
            visitor.FontFamilies);
    }

    private void CollectFromObject(
        CoreObject obj,
        string projectDirectory,
        IReadOnlySet<CoreObject>? stagedStorageObjects)
    {
        if (obj is EngineObject engineObj)
        {
            CollectFromEngineObject(engineObj, projectDirectory);
        }

        if (obj.Uri != null
            && stagedStorageObjects?.Contains(obj) != true
            && ShouldRelocateFile(obj.Uri, projectDirectory))
        {
            AddFileSource(obj, "Uri", obj.Uri);
        }

        var props = PropertyRegistry.GetRegistered(obj.GetType());
        foreach (var prop in props)
        {
            if (prop.PropertyType.IsValueType) continue;
            object? value = obj.GetValue(prop);
            switch (value)
            {
                case IFileSource fileSource:
                    if (fileSource.Uri != null && ShouldRelocateFile(fileSource.Uri, projectDirectory))
                    {
                        AddFileSource(obj, prop.Name, fileSource.Uri);
                    }

                    break;
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
            }
        }
    }

    private void CollectFromEngineObject(EngineObject obj, string projectDirectory)
    {
        foreach (IProperty property in obj.Properties)
        {
            switch (property.CurrentValue)
            {
                // Collect IFileSource
                case IFileSource fileSource when fileSource.Uri != null:
                    if (ShouldRelocateFile(fileSource.Uri, projectDirectory))
                    {
                        AddFileSource(obj, property.Name, fileSource.Uri);
                    }

                    break;
                // Collect FontFamily
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
            }
        }
    }

    private void AddFileSource(CoreObject owner, string propertyName, Uri uri)
    {
        _fileSources.Add((owner.Id, propertyName, uri));
        _relocationOwners.Add(owner);
    }

    /// <summary>
    /// Determines whether the URI must be copied into the package's resources directory.
    /// </summary>
    private static bool ShouldRelocateFile(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
            return false;

        string filePath = Path.GetFullPath(uri.LocalPath);
        string fullProjectPath = Path.GetFullPath(projectDirectory);
        string relativePath = Path.GetRelativePath(fullProjectPath, filePath);

        // Files outside the project directory are considered external.
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return true;
        }

        if (ContainsReservedPath(relativePath))
        {
            return true;
        }

        // Directory staging deliberately skips links. A referenced file that is itself a link,
        // or lives below a linked directory, must therefore go through the regular relocation
        // path so only that referenced target is materialized in resources. Inspect each lexical
        // component without resolving targets, which also identifies broken links and cycles.
        string currentPath = fullProjectPath;
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            currentPath = Path.Combine(currentPath, segments[i]);
            FileSystemInfo info = i == segments.Length - 1
                ? new FileInfo(currentPath)
                : new DirectoryInfo(currentPath);

            try
            {
                if (info.LinkTarget is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
                when (ex is IOException
                      or UnauthorizedAccessException
                      or NotSupportedException)
            {
                // Conservatively relocate when link inspection is unavailable. The relocation
                // service will either copy the referenced file or report it as a partial failure.
                return true;
            }
        }

        return false;
    }

    internal static bool IsInReservedProjectPath(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
        {
            return false;
        }

        string filePath = Path.GetFullPath(uri.LocalPath);
        string fullProjectPath = Path.GetFullPath(projectDirectory);
        string relativePath = Path.GetRelativePath(fullProjectPath, filePath);
        if (Path.IsPathFullyQualified(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsReservedPath(relativePath);
    }

    private static bool ContainsReservedPath(string relativePath)
    {
        string[] segments = relativePath.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(static segment =>
        {
            int streamSeparator = segment.IndexOf(':');
            string portableName = (streamSeparator >= 0 ? segment[..streamSeparator] : segment)
                .TrimEnd(' ', '.');
            return string.Equals(portableName, ".git", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(portableName, ".beutl", StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class SerializationGraphVisitor
    {
        private readonly List<CoreObject> _objects = [];
        private readonly HashSet<Uri> _unaddressableFileSources = [];
        private readonly HashSet<FontFamily> _fontFamilies = [];
        private readonly HashSet<object> _visitedCoreObjects = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _visitedCoreCollections = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, HashSet<Type>> _visitedContracts
            = new(ReferenceEqualityComparer.Instance);
        private readonly JsonSerializerOptions _passthroughOptions;
        private readonly JsonSerializerOptions _captureOptions;

        public SerializationGraphVisitor()
        {
            _passthroughOptions = new JsonSerializerOptions(JsonHelper.SerializerOptions);
            _captureOptions = new JsonSerializerOptions(_passthroughOptions);
            _captureOptions.Converters.Insert(
                0,
                new CaptureJsonConverterFactory(this, _passthroughOptions));
            _passthroughOptions.MakeReadOnly(populateMissingResolver: true);
        }

        public IReadOnlyList<CoreObject> Objects => _objects;

        public IReadOnlySet<Uri> UnaddressableFileSources => _unaddressableFileSources;

        public IReadOnlySet<FontFamily> FontFamilies => _fontFamilies;

        public void Visit(object? value)
        {
            VisitCoreSerializedValue(
                value,
                value?.GetType() ?? typeof(object),
                fileSourceIsAddressable: false);
        }

        public void VisitSerializedValue<T>(ICoreSerializable owner, string name, T? value)
        {
            bool fileSourceIsAddressable = value is IFileSource
                                           && IsDirectFileSourceProperty(owner, name, value);

            if (name == "Setter"
                && value is JsonNode
                && owner is INodeMember { Property: { } property })
            {
                object? propertyValue = property.GetValue();
                VisitCoreSerializedValue(
                    propertyValue,
                    propertyValue?.GetType() ?? property.PropertyType,
                    fileSourceIsAddressable: false);
                if (property is IAnimatablePropertyAdapter { Animation: { } animation })
                {
                    VisitCoreSerializable(animation);
                }

                return;
            }

            if (TryVisitKnownRawJsonContract(owner, name, value))
            {
                return;
            }

            VisitCoreSerializedValue(value, typeof(T), fileSourceIsAddressable);
        }

        private void VisitCoreSerializedValue(
            object? value,
            Type declaredType,
            bool fileSourceIsAddressable)
        {
            if (value is null or string)
            {
                return;
            }

            if (value is JsonNode or JsonElement or JsonDocument)
            {
                throw new InvalidDataException(
                    "Cannot safely inspect raw serialized JSON for external resources.");
            }

            switch (value)
            {
                case IFileSource fileSource:
                    RecordFileSource(fileSource, fileSourceIsAddressable);
                    break;
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
                case Typeface typeface:
                    _fontFamilies.Add(typeface.FontFamily);
                    break;
                case ICoreSerializable serializable:
                    VisitCoreSerializable(serializable);
                    break;
                case IReference:
                    break;
                case IEnumerable enumerable:
                    VisitCoreEnumerable(enumerable, declaredType);
                    break;
                default:
                    VisitSystemTextJsonValue(value, declaredType);
                    break;
            }
        }

        private void VisitCoreSerializable(ICoreSerializable serializable)
        {
            if (!serializable.GetType().IsValueType && !_visitedCoreObjects.Add(serializable))
            {
                return;
            }

            if (serializable is CoreObject coreObject)
            {
                _objects.Add(coreObject);
            }

            var context = new SerializationGraphContext(this, serializable);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                serializable.Serialize(context);
                context.Complete();
            }

            // Hierarchy membership is the fallback for custom hierarchical implementations
            // whose children are not exposed by Serialize. Run it after the serialization
            // contract so a child already emitted under a declared contract wins.
            if (serializable is IHierarchical hierarchical)
            {
                foreach (IHierarchical child in hierarchical.HierarchicalChildren)
                {
                    bool childFileSourceIsAddressable = child is IFileSource
                                                        && IsDirectFileSourceValue(serializable, child);
                    VisitCoreSerializedValue(child, child.GetType(), childFileSourceIsAddressable);
                }
            }
        }

        private void VisitCoreEnumerable(IEnumerable enumerable, Type declaredType)
        {
            Type runtimeType = enumerable.GetType();
            Type elementType = ArrayTypeHelpers.GetElementType(runtimeType) ?? typeof(object);
            if (runtimeType.IsAssignableTo(typeof(IDictionary))
                && ArrayTypeHelpers.GetEntryType(runtimeType) is (Type keyType, Type valueType)
                && keyType == typeof(string))
            {
                if (valueType.IsValueType)
                {
                    VisitSystemTextJsonValue(enumerable, declaredType);
                    return;
                }

                if (!_visitedCoreCollections.Add(enumerable))
                {
                    return;
                }

                var dictionary = (IDictionary)enumerable;
                foreach (object? item in dictionary.Values)
                {
                    VisitCoreSerializedValue(item, valueType, fileSourceIsAddressable: false);
                }

                return;
            }

            if (!_visitedCoreCollections.Add(enumerable))
            {
                return;
            }

            foreach (object? item in enumerable)
            {
                VisitCoreSerializedValue(item, elementType, fileSourceIsAddressable: false);
            }
        }

        private void VisitSystemTextJsonValue(object value, Type declaredType)
        {
            Type contractType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (!TryEnterContract(value, contractType))
            {
                return;
            }

            JsonNode? node = JsonSerializer.SerializeToNode(value, contractType, _captureOptions);
            Type serializedType
                = _passthroughOptions.GetTypeInfo(contractType).Kind == JsonTypeInfoKind.Object
                    ? value.GetType()
                    : contractType;
            InspectRoundTrippedValue(
                node,
                contractType,
                _passthroughOptions,
                rootFileSourceIsAddressable: false,
                validateStableRoundTrip: true,
                baseUri: ThreadLocalSerializationContext.Current?.BaseUri,
                contractName: contractType.FullName ?? contractType.Name,
                serializedType: serializedType);
        }

        public void VisitSerializedNodeValue(
            ICoreSerializable owner,
            string name,
            Type declaredType,
            Type actualType,
            JsonNode? node)
        {
            if (node is null)
            {
                return;
            }

            if (IsRawJsonCarrier(declaredType) || IsRawJsonCarrier(actualType))
            {
                throw new InvalidDataException(
                    $"Cannot safely inspect raw serialized node '{name}' for external resources.");
            }

            if (owner is CoreObject coreObject
                && PropertyRegistry.FindRegistered(coreObject, name) is { } property)
            {
                object? value = coreObject.GetValue(property);
                if (value is null)
                {
                    return;
                }

                CorePropertyMetadata metadata
                    = property.GetMetadata<CorePropertyMetadata>(owner.GetType());
                MethodInfo? getSerializerOptions = metadata.GetType().GetMethod(
                    nameof(CorePropertyMetadata<object>.GetSerializerOptions),
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    Type.EmptyTypes,
                    modifiers: null);
                if (getSerializerOptions?.Invoke(metadata, null) is not JsonSerializerOptions options)
                {
                    throw new InvalidOperationException(
                        $"Cannot inspect the JSON converter for property '{name}'.");
                }

                bool fileSourceIsAddressable = value is IFileSource
                                               && IsDirectFileSourceProperty(owner, name, value);
                Type serializedContractType
                    = options.GetTypeInfo(declaredType).Kind == JsonTypeInfoKind.Object
                        ? value.GetType()
                        : declaredType;
                InspectRoundTrippedValue(
                    node,
                    declaredType,
                    options,
                    fileSourceIsAddressable,
                    validateStableRoundTrip: true,
                    baseUri: (owner as CoreObject)?.Uri,
                    contractName: name,
                    serializedType: serializedContractType);
                return;
            }

            Type inspectionType = declaredType == typeof(object) && actualType != typeof(object)
                ? actualType
                : declaredType;
            try
            {
                object? restored = CoreSerializer.DeserializeFromJsonNode(
                    node.DeepClone(),
                    inspectionType,
                    new CoreSerializerOptions { BaseUri = (owner as CoreObject)?.Uri });
                Type serializedType = inspectionType.IsAssignableFrom(actualType)
                    ? actualType
                    : inspectionType;
                SerializedContractInspection inspection = InspectSerializedContract(
                    node,
                    serializedType,
                    JsonHelper.SerializerOptions,
                    (owner as CoreObject)?.Uri,
                    fileSourceIsAddressable: false,
                    name);
                ValidateStableJsonRoundTrip(
                    node,
                    restored,
                    inspectionType,
                    JsonHelper.SerializerOptions,
                    name,
                    inspection.CapturedFileSource && inspection.IsComplete);
                var visited = new Dictionary<object, HashSet<RoundTripVisitKey>>(
                    ReferenceEqualityComparer.Instance);
                ScanRoundTrippedResources(
                    restored,
                    new ScanContract(inspectionType, JsonHelper.SerializerOptions),
                    fileSourceIsAddressable: false,
                    visited,
                    opaqueAncestor: false);
            }
            catch (Exception ex) when (ex is JsonException
                                       or NotSupportedException
                                       or InvalidOperationException)
            {
                throw new InvalidDataException(
                    $"Cannot inspect serialized node '{name}' for external resources.",
                    ex);
            }
        }

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

                if (!fileSourceIsAddressable)
                {
                    _unaddressableFileSources.Add(uri);
                }

                return new SerializedContractInspection(
                    CapturedFileSource: true,
                    IsComplete: true);
            }

            if (contractType == typeof(FileInfo) || contractType == typeof(DirectoryInfo))
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

            if (IsRawJsonCarrier(contractType))
            {
                throw new InvalidDataException(
                    $"Cannot safely inspect raw serialized node '{contractName}' for external resources.");
            }

            JsonTypeInfo contractTypeInfo = options.GetTypeInfo(contractType);
            if (contractTypeInfo.Kind == JsonTypeInfoKind.None)
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

            if (node is JsonArray array)
            {
                Type? elementType = ArrayTypeHelpers.GetElementType(contractType);
                if (elementType is null)
                {
                    return default;
                }

                SerializedContractInspection inspection = SerializedContractInspection.Complete;
                foreach (JsonNode? item in array)
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

            if (node is not JsonObject jsonObject)
            {
                return SerializedContractInspection.Complete;
            }

            if (typeof(IDictionary).IsAssignableFrom(contractType)
                && ArrayTypeHelpers.GetEntryType(contractType)
                    is (Type keyType, Type valueType)
                && keyType == typeof(string))
            {
                SerializedContractInspection inspection = SerializedContractInspection.Complete;
                foreach ((string _, JsonNode? item) in jsonObject)
                {
                    inspection = inspection.Combine(InspectSerializedContract(
                        item,
                        valueType,
                        options,
                        baseUri,
                        fileSourceIsAddressable: false,
                        contractName));
                }

                return inspection;
            }

            JsonTypeInfo typeInfo = contractTypeInfo;
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return default;
            }

            string? discriminator = typeInfo.PolymorphismOptions
                ?.TypeDiscriminatorPropertyName;
            string discriminatorName = discriminator ?? "$type";
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
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    return;
                case Typeface typeface:
                    _fontFamilies.Add(typeface.FontFamily);
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
                    foreach (FieldInfo field in coreObjectFields)
                    {
                        ScanContract fieldContract = coreObjectFieldContracts.GetValueOrDefault(
                            field,
                            new ScanContract(field.FieldType, contract.Options));
                        ScanRoundTrippedResources(
                            field.GetValue(value),
                            fieldContract,
                            fileSourceIsAddressable: false,
                            visited,
                            opaquePath);
                    }
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

                if (opaqueContract)
                {
                    throw new InvalidDataException(
                        $"Cannot safely inspect opaque dictionary contract '{type.FullName}'.");
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

                if (opaquePath)
                {
                    throw new InvalidDataException(
                        $"Cannot safely inspect opaque collection contract '{type.FullName}'.");
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
                foreach (FieldInfo field in fields)
                {
                    ScanContract fieldContract = fieldContracts.GetValueOrDefault(
                        field,
                        new ScanContract(field.FieldType, contract.Options));
                    ScanRoundTrippedResources(
                        field.GetValue(value),
                        fieldContract,
                        fileSourceIsAddressable: false,
                        visited,
                        opaquePath);
                }
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
                    || !MayContainExternalResource(property.PropertyType))
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

        private bool TryVisitKnownRawJsonContract(
            ICoreSerializable owner,
            string name,
            object? value)
        {
            if (owner is EngineObject engineObject
                && name == "Expressions"
                && value is Dictionary<string, JsonNode> expressions)
            {
                int visitedExpressions = 0;
                foreach (IProperty property in engineObject.Properties)
                {
                    if (!expressions.ContainsKey(property.Name))
                    {
                        continue;
                    }

                    if (property.Expression is not { } expression)
                    {
                        return false;
                    }

                    Type expressionType = expression.GetType();
                    bool isBuiltInResourceFreeExpression = expressionType.IsGenericType
                        && expressionType.GetGenericTypeDefinition()
                            is var genericDefinition
                        && (genericDefinition
                                == typeof(Beutl.Engine.Expressions.StringExpression<>)
                            || genericDefinition
                                == typeof(Beutl.Engine.Expressions.ReferenceExpression<>));
                    if (!isBuiltInResourceFreeExpression)
                    {
                        VisitSystemTextJsonValue(expression, expressionType);
                    }

                    visitedExpressions++;
                }

                return visitedExpressions == expressions.Count;
            }

            return owner is Beutl.Animation.KeyFrame
                   && name == nameof(Beutl.Animation.KeyFrame.Easing)
                   && value is JsonObject easing
                   && easing.Count == 4
                   && IsJsonNumber(easing["X1"])
                   && IsJsonNumber(easing["Y1"])
                   && IsJsonNumber(easing["X2"])
                   && IsJsonNumber(easing["Y2"]);
        }

        private static bool IsJsonNumber(JsonNode? node)
        {
            return node is JsonValue value
                   && (value.TryGetValue(out float _)
                       || value.TryGetValue(out double _)
                       || value.TryGetValue(out decimal _));
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

        private void CaptureOpaqueFileUris(JsonNode? node, Uri? baseUri)
        {
            switch (node)
            {
                case JsonValue value when value.TryGetValue(out string? text):
                    CaptureOpaqueFileUri(text, baseUri, allowExtensionlessRelative: true);
                    break;
                case JsonArray array:
                    foreach (JsonNode? item in array)
                    {
                        CaptureOpaqueFileUris(item, baseUri);
                    }

                    break;
                case JsonObject jsonObject:
                    foreach ((string name, JsonNode? item) in jsonObject)
                    {
                        CaptureOpaqueFileUri(name, baseUri, allowExtensionlessRelative: false);
                        CaptureOpaqueFileUris(item, baseUri);
                    }

                    break;
            }
        }

        private void CaptureOpaqueFileUri(
            string? value,
            Uri? baseUri,
            bool allowExtensionlessRelative)
        {
            if (TryResolveOpaqueFileUri(
                    value,
                    baseUri,
                    allowExtensionlessRelative,
                    requireFilePath: false,
                    out Uri? uri))
            {
                _unaddressableFileSources.Add(uri);
            }
            else if (!IsAbsoluteNonFileUri(value) && LooksLikeFilePath(value))
            {
                throw new InvalidDataException(
                    $"Cannot resolve opaque serialized file path '{value}'.");
            }
        }

        private static bool TryResolveOpaqueFileUri(
            string? value,
            Uri? baseUri,
            bool allowExtensionlessRelative,
            bool requireFilePath,
            [NotNullWhen(true)] out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (LooksLikeWindowsPath(value))
            {
                string normalized = value.Replace('\\', '/')
                    .Replace("#", "%23", StringComparison.Ordinal)
                    .Replace("?", "%3F", StringComparison.Ordinal);
                return Uri.TryCreate($"file:///{normalized}", UriKind.Absolute, out uri);
            }

            if (Path.IsPathFullyQualified(value))
            {
                string fullPath = Path.GetFullPath(value);
                if (!File.Exists(fullPath)
                    && !Directory.Exists(fullPath)
                    && !LooksLikeFilePath(value)
                    && !requireFilePath)
                {
                    return false;
                }

                uri = CreateFileUri(fullPath);
                return true;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absoluteUri))
            {
                if (!absoluteUri.IsFile)
                {
                    return false;
                }

                uri = CanonicalizeFileUri(absoluteUri);
                return true;
            }

            if (baseUri is { IsFile: true })
            {
                try
                {
                    string? directory = Path.GetDirectoryName(baseUri.LocalPath);
                    if (directory is not null)
                    {
                        string rawPath = Path.GetFullPath(Path.Combine(directory, value));
                        if (File.Exists(rawPath) || Directory.Exists(rawPath))
                        {
                            uri = CreateFileUri(rawPath);
                            return true;
                        }
                    }
                }
                catch (Exception ex) when (ex is ArgumentException
                                           or IOException
                                           or NotSupportedException)
                {
                    // Fall through to the URI-based check and fail closed for path-like data.
                }
            }

            bool looksLikeFilePath = LooksLikeFilePath(value);
            string relativeReference = value
                .Replace("#", "%23", StringComparison.Ordinal)
                .Replace("?", "%3F", StringComparison.Ordinal);
            if ((!allowExtensionlessRelative && !looksLikeFilePath)
                || baseUri is null
                || !Uri.TryCreate(baseUri, relativeReference, out Uri? resolved)
                || !resolved.IsFile)
            {
                return false;
            }

            if (!looksLikeFilePath
                && !File.Exists(resolved.LocalPath)
                && !Directory.Exists(resolved.LocalPath)
                && !requireFilePath)
            {
                return false;
            }

            uri = CanonicalizeFileUri(resolved);
            return true;
        }

        private static bool IsAbsoluteNonFileUri(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && !LooksLikeWindowsPath(value)
                   && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                   && !uri.IsFile;
        }

        private static Uri CanonicalizeFileUri(Uri uri)
        {
            return CreateFileUri(uri.LocalPath);
        }

        private static Uri CreateFileUri(string path)
        {
            return new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Host = string.Empty,
                Path = Path.GetFullPath(path),
            }.Uri;
        }

        private static bool LooksLikeFilePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || LooksLikeNumericSerialization(value))
            {
                return false;
            }

            string extension = Path.GetExtension(value);
            return LooksLikeWindowsPath(value)
                   || value.StartsWith("./", StringComparison.Ordinal)
                   || value.StartsWith("../", StringComparison.Ordinal)
                   || value.StartsWith(".\\", StringComparison.Ordinal)
                   || value.StartsWith("..\\", StringComparison.Ordinal)
                   || extension.Length > 1 && extension.Skip(1).Any(char.IsLetter);
        }

        private static bool LooksLikeNumericSerialization(string value)
        {
            string candidate = value.Trim().Trim('<', '>', '(', ')', '[', ']');
            if (double.TryParse(
                    candidate,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return true;
            }

            string[] parts = candidate.Split(
                [',', '/', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 1
                   && parts.All(part => double.TryParse(
                       part,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out _));
        }

        private static bool LooksLikeWindowsPath(string value)
        {
            return value.Length >= 3
                   && char.IsAsciiLetter(value[0])
                   && value[1] == ':'
                   && value[2] is '/' or '\\';
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

        private void VisitCapturedJsonValue(object? value)
        {
            switch (value)
            {
                case IFileSource fileSource:
                    RecordFileSource(fileSource, fileSourceIsAddressable: false);
                    break;
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
                case Typeface typeface:
                    _fontFamilies.Add(typeface.FontFamily);
                    break;
                case IOptional { HasValue: true } optional:
                    {
                        object? optionalValue = optional.ToObject().Value;
                        if (optionalValue is ICoreSerializable serializable)
                        {
                            // OptionalJsonConverter deliberately calls SerializeToJsonObject here,
                            // even when the value also implements IFileSource.
                            VisitCoreSerializable(serializable);
                        }
                        else if (optionalValue is not null)
                        {
                            VisitSystemTextJsonValue(optionalValue, optional.GetValueType());
                        }

                        break;
                    }
                case ICoreSerializable serializable:
                    VisitCoreSerializable(serializable);
                    break;
            }
        }

        private void RecordFileSource(IFileSource fileSource, bool fileSourceIsAddressable)
        {
            Uri? uri;
            try
            {
                uri = fileSource.Uri;
            }
            catch (InvalidOperationException)
            {
                // Some interface-level JSON contracts reconstruct an empty placeholder.
                // The capture converter already observed the source that was actually written.
                return;
            }

            if (!fileSourceIsAddressable && uri != null)
            {
                _unaddressableFileSources.Add(uri);
            }
        }

        private bool TryEnterContract(object value, Type contractType)
        {
            if (value.GetType().IsValueType)
            {
                return true;
            }

            if (!_visitedContracts.TryGetValue(value, out HashSet<Type>? contracts))
            {
                contracts = [];
                _visitedContracts.Add(value, contracts);
            }

            return contracts.Add(contractType);
        }

        private sealed class CaptureJsonConverterFactory(
            SerializationGraphVisitor visitor,
            JsonSerializerOptions passthroughOptions) : JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert)
                => typeToConvert.IsAssignableTo(typeof(IFileSource))
                   || typeToConvert.IsAssignableTo(typeof(ICoreSerializable))
                   || typeToConvert.IsAssignableTo(typeof(IOptional))
                   || typeToConvert.IsAssignableTo(typeof(FontFamily))
                   || typeToConvert == typeof(Typeface);

            public override JsonConverter CreateConverter(
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                Type converterType = typeof(CaptureJsonConverter<>).MakeGenericType(typeToConvert);
                return (JsonConverter)Activator.CreateInstance(
                    converterType,
                    visitor,
                    passthroughOptions)!;
            }
        }

        private sealed class CaptureJsonConverter<T>(
            SerializationGraphVisitor visitor,
            JsonSerializerOptions passthroughOptions) : JsonConverter<T>
        {
            public override T? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
                => JsonSerializer.Deserialize<T>(ref reader, passthroughOptions);

            public override void Write(
                Utf8JsonWriter writer,
                T value,
                JsonSerializerOptions options)
            {
                visitor.VisitCapturedJsonValue(value);
                JsonSerializer.Serialize(writer, value, typeof(T), passthroughOptions);
            }
        }

        private static bool IsDirectFileSourceProperty(
            ICoreSerializable owner,
            string propertyName,
            object value)
        {
            if (owner is EngineObject engineObject
                && engineObject.Properties.FirstOrDefault(property => property.Name == propertyName)
                    is { CurrentValue: IFileSource currentValue }
                && ReferenceEquals(currentValue, value))
            {
                return true;
            }

            if (owner is CoreObject coreObject
                && PropertyRegistry.FindRegistered(coreObject, propertyName) is { } property
                && ReferenceEquals(coreObject.GetValue(property), value))
            {
                return true;
            }

            return false;
        }

        private static bool IsDirectFileSourceValue(object owner, object value)
        {
            if (owner is EngineObject engineObject
                && engineObject.Properties.Any(property => ReferenceEquals(property.CurrentValue, value)))
            {
                return true;
            }

            if (owner is CoreObject coreObject)
            {
                return PropertyRegistry.GetRegistered(coreObject.GetType())
                    .Any(property => ReferenceEquals(coreObject.GetValue(property), value));
            }

            return false;
        }
    }

    private sealed class SerializationGraphContext(
        SerializationGraphVisitor visitor,
        ICoreSerializable owner) : IJsonSerializationContext
    {
        private readonly JsonObject _json = [];
        private readonly Dictionary<string, (Type DefinedType, Type ActualType)>
            _pendingNodes = [];
        private readonly Dictionary<string, object?> _values = [];

        public CoreSerializationMode Mode
            => CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects;

        public Uri? BaseUri => (owner as CoreObject)?.Uri;

        public Type OwnerType => owner.GetType();

        public JsonObject GetJsonObject()
        {
            throw new InvalidDataException(
                "Cannot safely expose mutable serialized JSON during resource inspection.");
        }

        public void SetJsonObject(JsonObject obj)
        {
            throw new InvalidDataException(
                "Cannot safely inspect raw serialized JSON for external resources.");
        }

        public JsonNode? GetNode(string name)
        {
            throw new InvalidDataException(
                "Cannot safely expose mutable serialized JSON during resource inspection.");
        }

        public void SetNode(string name, Type definedType, Type actualType, JsonNode? node)
        {
            _values.Remove(name);
            _json[name] = node;
            _pendingNodes[name] = (definedType, actualType);
        }

        public void SetValue<T>(string name, T? value)
        {
            if (value is System.Reactive.Unit)
            {
                _values.Remove(name);
                _pendingNodes.Remove(name);
                _json.Remove(name);
                return;
            }

            visitor.VisitSerializedValue(owner, name, value);
            _pendingNodes.Remove(name);
            _json.Remove(name);
            _values[name] = value;
        }

        public T? GetValue<T>(string name)
        {
            if (_values.TryGetValue(name, out object? value))
            {
                if (value is null)
                {
                    return default;
                }

                if (value is T typed)
                {
                    return typed;
                }

                throw new InvalidDataException(
                    $"Cannot reproduce typed serialization read for '{name}'.");
            }

            if (_pendingNodes.ContainsKey(name))
            {
                throw new InvalidDataException(
                    $"Cannot reproduce serialized node read for '{name}'.");
            }

            return default;
        }

        public bool Contains(string name)
        {
            return _values.ContainsKey(name) || _pendingNodes.ContainsKey(name);
        }

        public void Populate(string name, ICoreSerializable obj)
        {
            visitor.Visit(obj);
        }

        public void Resolve(Guid id, Action<ICoreSerializable> callback)
        {
        }

        public void Complete()
        {
            if (_json.Count != _pendingNodes.Count
                || _json.Any(item => !_pendingNodes.ContainsKey(item.Key)))
            {
                throw new InvalidDataException(
                    "Serialized JSON was mutated outside a typed serialization contract.");
            }

            foreach ((string name, (Type definedType, Type actualType)) in _pendingNodes)
            {
                visitor.VisitSerializedNodeValue(
                    owner,
                    name,
                    definedType,
                    actualType,
                    _json[name]);
            }
        }
    }

    internal sealed record SerializationGraph(
        IReadOnlyList<CoreObject> Objects,
        IReadOnlySet<Uri> UnaddressableFileSources,
        IReadOnlySet<FontFamily> FontFamilies);
}
