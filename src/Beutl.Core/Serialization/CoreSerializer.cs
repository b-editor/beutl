using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public record CoreSerializerOptions
{
    public Uri? BaseUri { get; init; }

    public CoreSerializationMode? Mode { get; init; }
}

public static class CoreSerializer
{
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
        var type = obj.GetType();
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, options: options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Serialize(context);
            var jsonObject = context.GetJsonObject();
            jsonObject.WriteDiscriminator(type);
            return jsonObject;
        }
    }

    public static string SerializeToJsonString<T>(T obj, CoreSerializerOptions? options = null)
        where T : ICoreSerializable
    {
        return ConvertToJsonString(SerializeToJsonObject(obj, options));
    }

    public static string SerializeToJsonString(ICoreSerializable obj, CoreSerializerOptions? options = null)
    {
        return ConvertToJsonString(SerializeToJsonObject(obj));
    }

    public static string ConvertToJsonString(JsonObject jsonNode)
    {
        return jsonNode.ToJsonString(JsonHelper.SerializerOptions);
    }

    public static object DeserializeFromJsonObject(JsonObject json, Type baseType, CoreSerializerOptions? options = null)
    {
        // A sealed baseType deliberately ignores any present discriminator: sealed wrapper types
        // (e.g. Optional<T>) legitimately carry the wrapped payload's $type on their own node and
        // interpret it themselves during Deserialize.
        Type? actualType = baseType.IsSealed ? baseType : json.GetDiscriminator(baseType);
        if (actualType == null)
        {
            throw new InvalidOperationException("Discriminator not found in JSON object.");
        }

        try
        {
            if (!baseType.IsAssignableFrom(actualType))
            {
                throw new InvalidCastException(
                    $"Discriminator type '{actualType}' is not assignable to the expected type '{baseType}'.");
            }

            var obj = Activator.CreateInstance(actualType) as ICoreSerializable
                      ?? throw new InvalidOperationException($"Could not create instance of type {actualType.FullName}.");

            var parentContext = ThreadLocalSerializationContext.Current;
            ReflectUri(json, obj, parentContext, ref options);

            var context = new JsonSerializationContext(actualType, parentContext, json, options);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                obj.Deserialize(context);
                context.AfterDeserialized(obj);
            }

            if (obj is IFallback fallbackObj)
            {
                fallbackObj.Reason = FallbackReason.TypeNotFound;
                DeserializationIncidents.RecordFallback(fallbackObj);
            }

            return obj;
        }
        catch (Exception ex) when (FallbackDeserializationHelper.TryCreateFallback(
            baseType, actualType, json, ex) is { } fallback)
        {
            return fallback;
        }
    }

    // CoreObjectにUriを反映させ，CoreSerializerOptionsのBaseUriも更新する
    internal static void ReflectUri(JsonObject json, ICoreSerializable obj, ICoreSerializationContext? parent, ref CoreSerializerOptions? options)
    {
        var baseUri = options?.BaseUri ?? parent?.BaseUri;
        if (json["Uri"] is JsonValue uriValue && uriValue.TryGetValue(out string? uriString))
        {
            uriString = Uri.UnescapeDataString(uriString);
            var uri = baseUri != null
                ? new Uri(baseUri, uriString)
                : new Uri(uriString, UriKind.RelativeOrAbsolute);
            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }
            options ??= new CoreSerializerOptions { BaseUri = uri, Mode = options?.Mode };
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
        var parentContext = ThreadLocalSerializationContext.Current;
        ReflectUri(json, obj, parentContext, ref options);

        var context = new JsonSerializationContext(type, parentContext, json, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
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

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();

        // 互換性処理
        // 1.x で作成されたファイルでは一部のオブジェクトに $type が付与されないため、
        // 期待される型に基づいてディスクリミネータを補完する。
        // Presence is checked on the property key alone: a present-but-unparsable or non-string
        // discriminator must fail as an unknown type, not silently deserialize as the legacy
        // default and overwrite the original data on the next save.
        if (!jsonObject.ContainsKey("$type") && !jsonObject.ContainsKey("@type"))
        {
            if (type == typeof(ProjectItem))
            {
                node["$type"] = LegacyTypeNames.SceneDiscriminator;
            }
            else if (type.FullName == LegacyTypeNames.ElementFullName)
            {
                node["$type"] = LegacyTypeNames.ElementDiscriminator;
            }
        }

        bool hasDiscriminator = jsonObject.ContainsKey("$type") || jsonObject.ContainsKey("@type");
        Type? actualType = hasDiscriminator
            ? jsonObject.GetDiscriminator()
            : type.IsSealed ? type : jsonObject.GetDiscriminator(type);
        if (hasDiscriminator
            && actualType == null
            && FallbackDeserializationHelper.TryCreateFallback(type, null, jsonObject) is { } unknownTypeFallback)
        {
            ((IFallback)unknownTypeFallback).Reason = FallbackReason.TypeNotFound;
            if (unknownTypeFallback is CoreObject coreObject)
            {
                coreObject.Uri = uri;
            }

            return unknownTypeFallback;
        }

        if (actualType == null)
        {
            throw new InvalidOperationException("Discriminator not found in JSON object.");
        }

        if (!type.IsAssignableFrom(actualType))
        {
            // Reject before instantiating: deserializing the declared type first would run its own
            // load side effects (e.g. a Scene declared in a .belm globs and reopens element files).
            throw new InvalidCastException(
                $"Discriminator type '{actualType}' is not assignable to the expected type '{type}'.");
        }

        try
        {
            var obj = Activator.CreateInstance(actualType) as ICoreSerializable
                      ?? throw new InvalidOperationException($"Could not create instance of type {actualType.FullName}.");

            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }

            var options = new CoreSerializerOptions { BaseUri = uri, Mode = CoreSerializationMode.Read };
            PopulateFromJsonObject(obj, type, jsonObject, options);

            if (obj is IFallback fallbackObj)
            {
                fallbackObj.Reason = FallbackReason.TypeNotFound;
                DeserializationIncidents.RecordFallback(fallbackObj);
            }

            return obj;
        }
        catch (Exception ex) when (FallbackDeserializationHelper.TryCreateFallback(
            type, actualType, jsonObject, ex) is { } fallback)
        {
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

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();
        if (obj is CoreObject coreObj)
        {
            coreObj.Uri = uri;
        }

        var options = new CoreSerializerOptions { BaseUri = uri, Mode = CoreSerializationMode.Read };
        PopulateFromJsonObject(obj, type, jsonObject, options);
    }

    public static void StoreToUri<T>(T obj, Uri uri, CoreSerializationMode? mode = null)
        where T : ICoreSerializable
    {
        StoreToUriCore(obj, uri, mode, authorizedRootPath: null);
    }

    internal static void StoreToUri<T>(
        T obj,
        Uri uri,
        string authorizedRootPath,
        CoreSerializationMode? mode = null)
        where T : ICoreSerializable
    {
        StoreToUriCore(obj, uri, mode, authorizedRootPath);
    }

    private static void StoreToUriCore<T>(
        T obj,
        Uri uri,
        CoreSerializationMode? mode,
        string? authorizedRootPath)
        where T : ICoreSerializable
    {
        if (obj is CoreObject { SuppressedStorageSource: { } suppressed } suppressedObj)
        {
            if (uri == suppressed.SourceUri)
            {
                // The source location is skip-protected only while the on-disk bytes still match
                // the retained recovery bytes. A repair that was undone re-establishes the
                // suppression record through history with WasReinstated set, and the retained
                // bytes must be restored verbatim so the next open sees the same recovery state
                // the undo recorded. A continuously held record treats a mismatch as an external
                // repair of the sidecar and leaves the changed file alone — clobbering it would
                // destroy the user's repair.
                string sourcePath = uri.LocalPath;
                RestoreReinstatedBytes(suppressed, sourcePath);
                return;
            }

            if (uri.Scheme != "file")
            {
                throw new JsonException();
            }

            CopyReferencedStorageSources(suppressed, uri, authorizedRootPath);

            // Rehomed (save-as): the retained bytes move verbatim so the new project copy keeps the
            // element. SourceUri stays unchanged so the source location remains skip-protected if a
            // failed multi-file save rolls Uri back afterwards.
            string rehomedPath = uri.LocalPath;
            if (File.Exists(rehomedPath))
            {
                RestoreReinstatedBytes(suppressed, rehomedPath);
                suppressedObj.Uri = uri;
                return;
            }

            string? rehomedDirectory = Path.GetDirectoryName(rehomedPath);
            if (rehomedDirectory != null)
            {
                Directory.CreateDirectory(rehomedDirectory);
            }

            string tempPath = $"{rehomedPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(suppressed.RawBytes);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(tempPath, rehomedPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(rehomedPath))
                {
                    RestoreReinstatedBytes(suppressed, rehomedPath);
                    suppressedObj.Uri = uri;
                    return;
                }
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            suppressed.WasReinstated = false;
            suppressedObj.Uri = uri;
            return;
        }

        if (uri.Scheme == "file")
        {
            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }

            var path = uri.LocalPath;
            var directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            // tmp に書き出してから rename する。書き込み中のクラッシュや電源断で
            // 既存のプロジェクトファイル / Element ファイルがゼロバイト化したり
            // 中途半端な状態で残るのを防ぐ。
            // 固定 `.tmp` サフィックスだとユーザーや他ツールが既に持つ同名ファイルを
            // 上書きしてしまうため、ランダムサフィックスを付与して衝突を避ける。
            var options = new CoreSerializerOptions { BaseUri = uri, Mode = mode ?? CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects };
            string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions))
                {
                    SerializeToJsonObject(obj, options)
                        .WriteTo(writer, JsonHelper.SerializerOptions);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch
                {
                    // 失敗しても元の例外は投げる
                }
                throw;
            }
        }
        else
        {
            throw new JsonException();
        }
    }

    private static void CopyReferencedStorageSources(
        SuppressedStorageSource suppressed,
        Uri rehomedUri,
        string? authorizedRootPath)
    {
        if (suppressed.ReferencedStorageSources is not { Length: > 0 } referencedSources)
        {
            return;
        }

        if (suppressed.SourceRootPath is null)
        {
            throw new JsonException("Retained sidecars have no authorized source root.");
        }

        string destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            authorizedRootPath
            ?? Path.GetDirectoryName(rehomedUri.LocalPath)
            ?? throw new JsonException("Rehomed element has no destination directory.")));
        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var copies = new List<(SuppressedReferencedStorageSource Source, string Destination)>();
        foreach (SuppressedReferencedStorageSource source in referencedSources)
        {
            string relativePath = authorizedRootPath is null
                ? source.ElementRelativePath
                : source.RelativePath;
            if (Path.IsPathRooted(relativePath))
            {
                throw new JsonException($"Invalid retained sidecar path: {relativePath}");
            }

            string destination = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            if (!IsPathInsideRoot(destinationRoot, destination, comparison))
            {
                throw new JsonException($"Retained sidecar escapes the Save As root: {relativePath}");
            }

            copies.Add((source, destination));
        }

        foreach ((SuppressedReferencedStorageSource source, string destination) in copies)
        {
            WriteBytesAtomicallyIfMissing(destination, source.RawBytes);
        }
    }

    private static bool IsPathInsideRoot(
        string root,
        string candidate,
        StringComparison comparison)
    {
        string prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(candidate, root, comparison)
               || candidate.StartsWith(prefix, comparison);
    }

    private static void WriteBytesAtomicallyIfMissing(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(tempPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static void RestoreReinstatedBytes(SuppressedStorageSource suppressed, string path)
    {
        if (!suppressed.WasReinstated && File.Exists(path))
        {
            return;
        }

        if (!File.Exists(path)
            || !File.ReadAllBytes(path).AsSpan().SequenceEqual(suppressed.RawBytes))
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            WriteBytesAtomically(path, suppressed.RawBytes);
        }

        suppressed.WasReinstated = false;
    }

    private static void WriteBytesAtomically(string path, byte[] bytes)
    {
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}
