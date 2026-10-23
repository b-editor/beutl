using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.JsonConverters;

namespace Beutl.Serialization;

public record CoreSerializerOptions
{
    public Uri? BaseUri { get; init; }

    public CoreSerializationMode? Mode { get; init; }

    /// <summary>
    /// Reuses existing structured member values when populating an object with equal JSON.
    /// Missing or changed members retain their normal deserialization behavior.
    /// Built-in property readers skip assigning unchanged values, including collections.
    /// Custom collection reads deserialize normally so readers can safely clear and refill their destination.
    /// Preserved objects remain available for reference resolution.
    /// </summary>
    public bool PreserveUnchangedValues { get; init; }

    /// <summary>
    /// Whether reading keeps the members a type does not read and writing writes them back. Unset, a read or
    /// write does what the one around it does, which keeps them at the top.
    /// </summary>
    /// <remarks>
    /// The Beutl 1.x element migration drops them, since 2.x does not read what 1.x kept for itself. Agent
    /// documents leave them out and applying one keeps none of its own, since an agent edits only members Beutl
    /// knows. Populating with this off leaves the members an object kept before alone.
    /// </remarks>
    internal bool? KeepUnknownMembers { get; init; }

    // Embeds a copy, which holds each file-backed object itself but keeps referencing a project item's file.
    internal bool? EmbedsCopy { get; init; }
}

public static partial class CoreSerializer
{
    // The file-backed objects this thread is embedding, so a reference cycle through them ends at
    // its back edge the way t_activeWrites ends one for a real save.
    [ThreadStatic]
    private static HashSet<CoreObject>? t_activeEmbeds;

    /// <summary>
    /// Serializes a file-backed object into the node that references it, or returns
    /// <see langword="null"/> when this thread is already embedding it or a copy references its file.
    /// </summary>
    /// <remarks>
    /// A reference cycle through file-backed objects would otherwise recurse until the stack overflows,
    /// so the back edge keeps its URI, as it does in a real save.
    /// </remarks>
    internal static JsonObject? SerializeEmbeddedReference(
        CoreObject value, Uri serializedUri, ICoreSerializationContext parent)
    {
        if (t_activeEmbeds?.Contains(value) == true)
        {
            return null;
        }

        // A copy that kept the Uri would be saved over the original's file (#3288).
        if (parent is JsonSerializationContext { EmbedsCopy: true })
        {
            return value is ProjectItem ? null : SerializeToJsonObject(value);
        }

        JsonObject node = SerializeToJsonObject(value, new CoreSerializerOptions { BaseUri = value.Uri });
        node["Uri"] = UriHelper.ToPersistedReference(serializedUri);
        return node;
    }

    public static JsonNode SerializeToJsonNode(object obj, CoreSerializerOptions? options = null)
    {
        var ownerJson = new JsonObject();
        var context = new JsonSerializationContext(
            obj.GetType(), ThreadLocalSerializationContext.Current, ownerJson, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            context.SetValue("Value", obj);
        }

        var valueNode = ownerJson["Value"];
        ownerJson.Remove("Value");

        return valueNode!;
    }

    public static JsonObject SerializeToJsonObject(ICoreSerializable obj, CoreSerializerOptions? options = null)
    {
        return SerializeToJsonObject(obj, options, capturedValues: null);
    }

    private static JsonObject SerializeToJsonObject(ICoreSerializable obj, CoreSerializerOptions? options,
        Dictionary<string, JsonSerializationContext.CapturedValue>? capturedValues)
    {
        SerializedObjectCapture.Record(obj);
        var type = obj.GetType();
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, options: options)
        {
            CapturedValues = capturedValues
        };
        // Registered whether this is an embedded reference or the root of an embedding pass, so a back
        // edge to the root keeps its URI instead of embedding the root a second time.
        bool embedding = context.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects)
                         && obj is CoreObject { Uri: not null } fileBacked
                         && (t_activeEmbeds ??= new(ReferenceEqualityComparer.Instance)).Add(fileBacked);
        try
        {
            return SerializeToJsonObjectCore(obj, type, context);
        }
        finally
        {
            if (embedding)
            {
                t_activeEmbeds!.Remove((CoreObject)obj);
                if (t_activeEmbeds.Count == 0) t_activeEmbeds = null;
            }
        }
    }

    private static JsonObject SerializeToJsonObjectCore(ICoreSerializable obj, Type type, JsonSerializationContext context)
    {
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Serialize(context);
            context.WriteUnknownMembers(obj);
            // A fallback writes the discriminator it was read with, which names the type that could not be loaded;
            // replacing it with the fallback's own type would lose that type for good.
            if (obj is IFallback fallback)
            {
                context.WriteFallback(fallback);
                return context.GetJsonObject();
            }

            var jsonObject = context.GetJsonObject();
            jsonObject.WriteDiscriminator(type);
            return jsonObject;
        }
    }

    public static string SerializeToJsonString(ICoreSerializable obj, CoreSerializerOptions? options = null)
    {
        return ConvertToJsonString(SerializeToJsonObject(obj, options));
    }

    public static string ConvertToJsonString(JsonObject jsonNode)
    {
        return jsonNode.ToJsonString(JsonHelper.SerializerOptions);
    }

    public static object DeserializeFromJsonObject(JsonObject json, Type baseType, CoreSerializerOptions? options = null)
    {
        Type? actualType = ResolveActualType(json, baseType);
        if (actualType == null)
        {
            throw new InvalidOperationException("Discriminator not found in JSON object.");
        }

        try
        {
            ICoreSerializable obj = InstantiateDiscriminatedType(baseType, actualType);
            DeserializeInstance(obj, json, actualType, ThreadLocalSerializationContext.Current, options);
            return obj;
        }
        catch (Exception ex) when (FallbackDeserializationHelper.TryCreateFallback(
            baseType, actualType, json, ex) is { } fallback)
        {
            return fallback;
        }
    }

    // Decides the type that a node read as baseType is created as.
    internal static Type? ResolveActualType(JsonObject json, Type baseType)
    {
        // A sealed baseType deliberately ignores any present discriminator: sealed wrapper types
        // (e.g. Optional<T>) legitimately carry the wrapped payload's $type on their own node and
        // interpret it themselves during Deserialize.
        if (baseType.IsSealed)
        {
            return baseType;
        }

        // $type is written only for a declared type that is not sealed, so a node without one was written
        // while its declared type was sealed. Reading it as that type lets a type be unsealed without breaking
        // the files that hold it. A node that has a discriminator, even one that cannot be resolved, is not
        // read this way.
        if (!json.ContainsKey("$type") && CanReadAsDeclaredType(baseType))
        {
            return baseType;
        }

        return json.GetDiscriminator(baseType);
    }

    // Whether a node without a discriminator is created as baseType itself. That needs a concrete
    // ICoreSerializable, and not one whose own fallback can take its place, such as EngineObject: such a type
    // cannot be sealed, so its nodes are written with $type, and one without stays the fallback, which writes
    // it back unchanged instead of dropping every member the type does not know.
    private static bool CanReadAsDeclaredType(Type baseType)
    {
        if (baseType.IsAbstract || !baseType.IsAssignableTo(typeof(ICoreSerializable)))
        {
            return false;
        }

        return Attribute.GetCustomAttribute(baseType, typeof(FallbackTypeAttribute)) is not FallbackTypeAttribute attribute
               || !baseType.IsAssignableFrom(attribute.FallbackType);
    }

    // Creates the type a discriminator names once it is known to stand in for baseType.
    internal static ICoreSerializable InstantiateDiscriminatedType(Type baseType, Type actualType)
    {
        if (!baseType.IsAssignableFrom(actualType))
        {
            throw new InvalidCastException(
                $"Discriminator type '{actualType}' is not assignable to the expected type '{baseType}'.");
        }

        return Activator.CreateInstance(actualType) as ICoreSerializable
               ?? throw new InvalidOperationException($"Could not create instance of type {actualType.FullName}.");
    }

    // Deserializes a newly created instance under a child of parent. Callers read parent themselves, so
    // each keeps its own order between creating the instance and reading the ambient context.
    internal static void DeserializeInstance(
        ICoreSerializable obj,
        JsonObject json,
        Type actualType,
        ICoreSerializationContext? parent,
        CoreSerializerOptions? options)
    {
        ReflectUri(json, obj, parent, ref options);

        var context = new JsonSerializationContext(actualType, parent, json, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
            context.KeepUnknownMembers(obj);
            context.AfterDeserialized(obj);
        }

        MarkFallbackInstance(obj);
    }

    private static void MarkFallbackInstance(ICoreSerializable obj)
    {
        if (obj is IFallback fallbackObj)
        {
            fallbackObj.Reason = FallbackReason.TypeNotFound;
            DeserializationIncidents.RecordFallback(fallbackObj);
        }
    }

    // CoreObjectにUriを反映させ，CoreSerializerOptionsのBaseUriも更新する
    internal static void ReflectUri(JsonObject json, ICoreSerializable obj, ICoreSerializationContext? parent, ref CoreSerializerOptions? options)
    {
        var baseUri = options?.BaseUri ?? parent?.BaseUri;
        if (json["Uri"] is JsonValue uriValue && uriValue.TryGetValue(out string? uriString))
        {
            Uri uri = UriHelper.ResolvePersistedReference(uriString, baseUri, allowRelative: true);
            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }
            options ??= new CoreSerializerOptions { BaseUri = uri };
        }
    }

    public static object? DeserializeFromJsonNode(JsonNode json, Type type, CoreSerializerOptions? options = null)
    {
        var ownerJson = new JsonObject { ["Value"] = json.DeepClone() };
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, ownerJson, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            return context.GetValue("Value", type);
        }
    }

    public static void PopulateFromJsonObject<T>(T obj, JsonObject json, CoreSerializerOptions? options = null)
        where T : ICoreSerializable
    {
        PopulateFromJsonObject(obj, typeof(T), json, options);
    }

    public static void PopulateFromJsonObject(ICoreSerializable obj, Type type, JsonObject json,
        CoreSerializerOptions? options = null)
    {
        AddLegacyTypeDiscriminator(json, obj.GetType());
        PopulateFromJsonObjectCore(obj, type, json, options);
    }

    private static void PopulateFromJsonObjectCore(ICoreSerializable obj, Type type, JsonObject json,
        CoreSerializerOptions? options)
    {
        var parentContext = ThreadLocalSerializationContext.Current;
        ReflectUri(json, obj, parentContext, ref options);

        var context = new JsonSerializationContext(type, parentContext, json, options);
        if (options?.PreserveUnchangedValues == true && obj is not IFallback
            && json.Any(pair => pair.Value is JsonObject or JsonArray))
        {
            var capturedValues = new Dictionary<string, JsonSerializationContext.CapturedValue>();
            JsonObject current = SerializeToJsonObject(obj, new CoreSerializerOptions
            {
                BaseUri = context.BaseUri,
                Mode = context.Mode & ~CoreSerializationMode.SaveReferencedObjects,
                // Like json: JSON without the unknown members, such as an agent document, must find a value
                // that differs only in them unchanged, or the value is replaced by one without them.
                KeepUnknownMembers = context.KeepsUnknownMembers
            }, capturedValues);
            context.PreserveUnchangedValues(current, capturedValues);
        }
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
            context.KeepUnknownMembers(obj);
            context.AfterDeserialized(obj);
        }
    }

    public static T RestoreFromUri<T>(Uri uri)
        where T : ICoreSerializable
    {
        return (T)RestoreFromUri(uri, typeof(T));
    }

    public static object RestoreFromUri(Uri uri, Type type)
    {
        using var stream = UriHelper.ResolveStream(uri);
        JsonObject jsonObject = ParseStoredObject(stream, uri);
        StoredVersion version = TakeVersionFields(jsonObject, uri);
        ReadTimeTypesOfEarlierVersions(jsonObject, version);

        // 互換性処理
        // 1.x で作成されたファイルでは一部のオブジェクトに $type が付与されないため、
        // 期待される型に基づいてディスクリミネータを補完する。
        AddLegacyTypeDiscriminator(jsonObject, type);

        bool hasDiscriminator = jsonObject.ContainsKey("$type");
        Type? actualType = hasDiscriminator
            ? jsonObject.GetDiscriminator()
            : ResolveActualType(jsonObject, type);
        if (hasDiscriminator
            && actualType == null
            && FallbackDeserializationHelper.TryCreateFallback(type, null, jsonObject) is { } unknownTypeFallback)
        {
            ((IFallback)unknownTypeFallback).Reason = FallbackReason.TypeNotFound;
            return Adopt(unknownTypeFallback);
        }

        if (actualType == null)
        {
            throw new InvalidOperationException("Discriminator not found in JSON object.");
        }

        if (!type.IsAssignableFrom(actualType))
        {
            // Reject before instantiating: deserializing the declared type first would run its own
            // load side effects (e.g. a Scene declared in a .belm globs and reopens element files).
            var exception = new InvalidCastException(
                $"Discriminator type '{actualType}' is not assignable to the expected type '{type}'.");
            if (FallbackDeserializationHelper.TryCreateFallback(
                    type,
                    actualType,
                    jsonObject,
                    exception) is { } incompatibleTypeFallback)
            {
                return Adopt(incompatibleTypeFallback);
            }

            throw exception;
        }

        try
        {
            var obj = Activator.CreateInstance(actualType) as ICoreSerializable
                      ?? throw new InvalidOperationException($"Could not create instance of type {actualType.FullName}.");

            RecordRead(obj, version, uri);
            PopulateFromStorage(obj, type, jsonObject, uri);
            MarkFallbackInstance(obj);
            return obj;
        }
        catch (Exception ex) when (FallbackDeserializationHelper.TryCreateFallback(
            type, actualType, jsonObject, ex) is { } fallback)
        {
            return Adopt(fallback);
        }

        // A fallback stands in for the object the file holds, so it takes the file's Uri and version fields.
        ICoreSerializable Adopt(ICoreSerializable fallback)
        {
            if (fallback is CoreObject coreObject)
            {
                coreObject.Uri = uri;
            }

            RecordRead(fallback, version, uri);
            return fallback;
        }
    }

    public static void PopulateFromUri<T>(T obj, Uri uri)
        where T : ICoreSerializable
    {
        PopulateFromUri(obj, typeof(T), uri);
    }

    public static void PopulateFromUri(ICoreSerializable obj, Type type, Uri uri)
    {
        using var stream = UriHelper.ResolveStream(uri);
        JsonObject jsonObject = ParseStoredObject(stream, uri);
        StoredVersion version = TakeVersionFields(jsonObject, uri);
        ReadTimeTypesOfEarlierVersions(jsonObject, version);
        AddLegacyTypeDiscriminator(jsonObject, obj.GetType());
        RecordRead(obj, version, uri);
        PopulateFromStorage(obj, type, jsonObject, uri);
    }

    // The caller owns the stream, so the file stays open until the whole restore has finished.
    private static JsonObject ParseStoredObject(Stream stream, Uri uri)
    {
        ReferencedStorageCapture.Record(uri);

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();
        return jsonObject;
    }

    private static void PopulateFromStorage(
        ICoreSerializable obj,
        Type type,
        JsonObject json,
        Uri uri)
    {
        if (obj is CoreObject coreObj)
        {
            coreObj.Uri = uri;
        }

        var options = new CoreSerializerOptions { BaseUri = uri, Mode = CoreSerializationMode.Read };
        // The files a project references are read while it is, and record it as their project.
        using (RootFileVersions.BeginReading(obj as Project))
        using (ReadOnProjectFrameGrid(obj))
        {
            PopulateFromJsonObjectCore(obj, type, json, options);
        }

        if (obj is Project project)
        {
            DuplicateIds.Report(project, "load");
        }
    }

    // A file read into a project outside the read of the project itself, such as a reload, is read on its frame grid.
    private static IDisposable? ReadOnProjectFrameGrid(ICoreSerializable obj)
    {
        if (obj is Project || MediaTimeJsonConverter.ReadsOnFrameGrid)
            return null;

        Project? project = (obj as IHierarchical)?.FindHierarchicalParent<Project>() ?? RootFileVersions.GetProject(obj);
        return project is null ? null : MediaTimeJsonConverter.ReadOnFrameGrid(Project.ReadFrameRate(project));
    }

    private static void AddLegacyTypeDiscriminator(JsonObject json, Type type)
    {
        // Preserve present but invalid discriminators for unknown-type recovery.
        if (json.ContainsKey("$type"))
        {
            return;
        }

        if (type == typeof(ProjectItem) || type.FullName == "Beutl.ProjectSystem.Scene")
        {
            json["$type"] = LegacyTypeNames.SceneDiscriminator;
        }
        else if (type.FullName == LegacyTypeNames.ElementFullName)
        {
            json["$type"] = LegacyTypeNames.ElementDiscriminator;
        }
    }
}
