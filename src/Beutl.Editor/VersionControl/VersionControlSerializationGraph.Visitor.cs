using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.IO;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.Serialization;

namespace Beutl.Editor;

internal static partial class VersionControlSerializationGraph
{
    private sealed partial class SerializationGraphVisitor
    {
        private readonly List<CoreObject> _objects = [];
        private readonly HashSet<Uri> _unaddressableFileSources = [];
        private readonly HashSet<Uri> _addressableFileSources = [];
        private readonly HashSet<object> _visitedCoreObjects = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _visitedCoreCollections = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, HashSet<Type>> _visitedContracts
            = new(ReferenceEqualityComparer.Instance);
        private readonly JsonSerializerOptions _passthroughOptions;
        private readonly JsonSerializerOptions _captureOptions;
        private Uri? _currentFileUri;

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

        public IReadOnlySet<Uri> AddressableFileSources => _addressableFileSources;

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
                case FontFamily or Typeface:
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

            // CoreSerializer writes a file source relative to the nearest object that has a file of its
            // own, so an object saved inside another file resolves its paths against that file.
            Uri? outerFileUri = _currentFileUri;
            if (serializable is CoreObject { Uri: { } fileUri })
            {
                _currentFileUri = fileUri;
            }

            try
            {
                if (serializable is IFallback fallback)
                {
                    CaptureFallbackFileUris(fallback.Json, _currentFileUri);
                }
                else
                {
                    var context = new SerializationGraphContext(this, serializable);
                    using (ThreadLocalSerializationContext.Enter(context))
                    {
                        serializable.Serialize(context);
                        context.Complete();
                    }
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
            finally
            {
                _currentFileUri = outerFileUri;
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

            if (owner is Beutl.ProjectSystem.Scene
                && name is Beutl.ProjectSystem.SceneRecovery.RecoveredElementIdsKey
                    or Beutl.ProjectSystem.SceneRecovery.RecoveredDescendantIdsKey
                    or Beutl.ProjectSystem.SceneRecovery.RecoveredDescendantIdentitiesKey)
            {
                // Recovery metadata maps the scene's own children to identities. Those children are
                // serialized on their own, so an identity is all this JSON may carry.
                return value is JsonObject identities
                       && identities.All(static item => item.Value is JsonValue id
                                                        && id.TryGetValue(out string? text)
                                                        && Guid.TryParse(text, out _));
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

        private void VisitCapturedJsonValue(object? value)
        {
            switch (value)
            {
                case IFileSource fileSource:
                    RecordFileSource(fileSource, fileSourceIsAddressable: false);
                    break;
                case FontFamily or Typeface:
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

            if (uri != null && fileSourceIsAddressable)
            {
                _addressableFileSources.Add(uri);
            }
            else if (uri != null)
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
}
