using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Versioning;

namespace Beutl.Serialization;

public record CoreSerializerOptions
{
    public Uri? BaseUri { get; init; }

    public CoreSerializationMode? Mode { get; init; }
}

public static class CoreSerializer
{
    [ThreadStatic]
    private static Dictionary<ICoreSerializable, HashSet<Uri>>? t_activeWrites;

    // What StoreToUri writes with when its caller names no mode.
    private const CoreSerializationMode DefaultStoreMode =
        CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects;

    // Complete this preflight for the whole save before replacing any migrated sidecar.
    internal static void PersistProjectMigrationMetadata(IEnumerable<CoreObject> objects)
    {
        CoreObject[] pending = objects as CoreObject[] ?? objects.ToArray();

        var projects = new HashSet<Project>();
        foreach (CoreObject obj in pending)
        {
            if (obj is Project project)
                projects.Add(project);
            if (obj is IHierarchical hierarchical)
            {
                foreach (Project ancestor in hierarchical.EnumerateAncestors<Project>())
                    projects.Add(ancestor);
            }
        }

        RaiseAttachedMigrationsIfUncovered(pending, projects);

        foreach (Project project in projects)
        {
            if (project.Uri is not null)
            {
                WriteMigrationGate(project, project.Uri);
            }
        }
    }

    /// <summary>
    /// The same preflight for a project being written to a named destination, which is not always
    /// the URI it currently carries: a first save has none, and a Save As names a different one.
    /// </summary>
    private static void PersistProjectMigrationGate(Project project, Uri destination)
    {
        RaiseAttachedMigrationsIfUncovered([project], [project]);
        WriteMigrationGate(project, destination);
    }

    /// <summary>
    /// Discovers only while a guarded project's gate does not already cover every requirement a live
    /// value still carries.
    /// </summary>
    /// <remarks>
    /// That is what a value assigned to a new owner looks like from here, whether it is reaching its
    /// first owner or a second one, and it is equally why a graph that has taken its requirements
    /// over never pays for the pass again.
    /// </remarks>
    private static void RaiseAttachedMigrationsIfUncovered(
        CoreObject[] pending,
        IReadOnlyCollection<Project> projects)
    {
        if (AttachedContentMigrations.HighestRetained is { } outstanding
            && projects.Any(project => !CoversMigration(project, outstanding)))
        {
            RaiseAttachedMigrations(pending);
        }
    }

    private static void WriteMigrationGate(Project project, Uri destination)
    {
        string? required = null;
        foreach (ProjectItem item in project.Items)
        {
            required = Project.GetMaximumMigrationVersion(
                required,
                Project.GetRequiredMigrationVersion(item));
        }

        if (required is null)
        {
            return;
        }

        project.MarkAsMigrated(required);
        if (destination.Scheme != "file" || !TryWriteMigrationGate(project, destination.LocalPath))
        {
            StoreToUri(project, destination, CoreSerializationMode.Write);
        }
    }

    /// <summary>
    /// Puts the project's version metadata at <paramref name="path"/> without recording the item
    /// graph, and reports whether it could.
    /// </summary>
    /// <remarks>
    /// This preflight runs before the save it guards, and that save can still fail — after which
    /// <c>ProjectPersistence</c> rolls the in-memory item list back. Re-serializing the graph here
    /// would leave the file recording a graph that never happened and pointing at sidecars that were
    /// never written, so a file that is already there keeps the graph it records and a destination
    /// with none gets the gate alone, which points at nothing. A location that cannot hold the gate
    /// is not skipped: its failure stops the save before any sidecar is replaced.
    /// </remarks>
    private static bool TryWriteMigrationGate(Project project, string path)
    {
        JsonObject json;
        if (File.Exists(path))
        {
            JsonNode? node;
            try
            {
                using FileStream stream = File.OpenRead(path);
                node = JsonNode.Parse(stream);
            }
            catch (JsonException)
            {
                // Unreadable bytes are not a graph worth preserving, and refusing here would leave
                // a malformed destination unsaveable. Only a failure to reach the file at all is
                // allowed to stop the save.
                return false;
            }

            // Nothing to raise in place; the caller writes the file the ordinary way.
            if (node is not JsonObject existing)
            {
                return false;
            }

            json = existing;
        }
        else
        {
            json = [];
            json.WriteDiscriminator(typeof(Project));
        }

        // A gate already on disk is never lowered, the way Project.MarkAsMigrated treats the one it
        // loaded: a Save As can name a file whose own constraint is higher than this project's.
        string persisted = (string?)json["minAppVersion"] ?? Project.DefaultMinAppVersion;
        json["minAppVersion"] = Project.GetMaximumVersion(persisted, project.MinAppVersion);
        json["appVersion"] = project.AppVersion;

        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions))
            {
                json.WriteTo(writer, JsonHelper.SerializerOptions);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            StorageWriteTransaction.MoveIntoPlace(
                temporaryPath,
                path,
                overwrite: true,
                isCompatibilityGate: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // 失敗しても元の例外は投げる
            }

            throw;
        }

        return true;
    }

    // Unparseable versions answer "covered" rather than throwing: an unknown persisted constraint is
    // retained rather than weakened (see Project.MarkAsMigrated), so nothing this pass could
    // discover would change the gate, and a save must not fail over a value it cannot read.
    private static bool CoversMigration(Project project, string requiredVersion)
    {
        if (!NuGetVersion.TryParse(requiredVersion, out NuGetVersion? required)
            || !NuGetVersion.TryParse(project.MinAppVersion, out NuGetVersion? gate))
        {
            return true;
        }

        if (VersionComparer.VersionRelease.Compare(gate, required) >= 0)
        {
            return true;
        }

        foreach (ProjectItem item in project.Items)
        {
            if (Project.GetRequiredMigrationVersion(item) is { } known
                && NuGetVersion.TryParse(known, out NuGetVersion? knownVersion)
                && VersionComparer.VersionRelease.Compare(knownVersion, required) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Raises the migration requirement of every value that reports one only while its owner is
    /// written, so the preflight above still runs before the files carrying those values.
    /// </summary>
    /// <remarks>
    /// A value deserialized on its own hands its requirement to its owner during serialization, and
    /// waiting for the real write would put the compatibility gate behind the sidecars it guards.
    /// Serializing to a discarded buffer first is the same discovery <c>SceneRecovery</c> performs,
    /// and nothing reaches the disk: <see cref="CoreSerializationMode.Write"/> on its own leaves a
    /// referenced object as a URI, and the caller runs this only while a gate about to be written
    /// does not already cover every requirement a live value carries.
    /// </remarks>
    private static void RaiseAttachedMigrations(CoreObject[] objects)
    {
        var visited = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CoreObject Object, bool OwnsFile)>();
        foreach (CoreObject obj in objects)
        {
            pending.Push((obj, true));
        }

        while (pending.TryPop(out (CoreObject Object, bool OwnsFile) current))
        {
            if (!visited.Add(current.Object))
            {
                continue;
            }

            // Only a root of this save and a descendant with a file of its own are written on their
            // own; everything else is embedded in the nearest such ancestor and covered by its pass.
            if (current.OwnsFile)
            {
                SerializeToJsonObject(
                    current.Object,
                    new CoreSerializerOptions
                    {
                        BaseUri = current.Object.Uri,
                        Mode = CoreSerializationMode.Write,
                    });
            }

            if (current.Object is IHierarchical hierarchical)
            {
                foreach (IHierarchical child in hierarchical.HierarchicalChildren)
                {
                    if (child is CoreObject coreObject)
                    {
                        pending.Push((coreObject, coreObject.Uri is not null));
                    }
                }
            }
        }
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
        SerializedObjectCapture.Record(obj);
        var type = obj.GetType();
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, options: options);
        context.BeginSerialization(obj);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Serialize(context);
            // A System.Text.Json converter routes a nested value through this entry point instead of
            // SerializeCoreSerializable — an Optional<T> holding one, or a property whose declared
            // type sends it to CoreSerializableJsonConverter — so the requirement is handed to the
            // ambient owner here as well. A save that starts here has no parent and skips it.
            JsonSerializationContext.TransferRetainedMigration(obj, context.Parent);
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
            context.EnablePersistedContentMigrationReporting();
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
            Uri uri = UriHelper.ResolvePersistedReference(uriString, baseUri, allowRelative: true);
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
        context.EnablePersistedContentMigrationReporting();
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
        bool addedTypeDiscriminator = AddLegacyTypeDiscriminator(json, obj.GetType());
        PopulateFromJsonObjectCore(obj, type, json, options, addedTypeDiscriminator);
    }

    private static void PopulateFromJsonObjectCore(ICoreSerializable obj, Type type, JsonObject json,
        CoreSerializerOptions? options, bool addedTypeDiscriminator)
    {
        var parentContext = ThreadLocalSerializationContext.Current;
        ReflectUri(json, obj, parentContext, ref options);

        var context = new JsonSerializationContext(type, parentContext, json, options);
        context.EnablePersistedContentMigrationReporting();
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
            if (addedTypeDiscriminator)
            {
                context.ReportPersistedContentMigration(Project.DefaultMinAppVersion);
            }
            context.AfterDeserialized(obj);
        }
        if (addedTypeDiscriminator && obj is CoreObject coreObject)
        {
            coreObject.WasTypeDiscriminatorAddedDuringRestore = true;
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
        ReferencedStorageCapture.Record(uri);

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();

        // 互換性処理
        // 1.x で作成されたファイルでは一部のオブジェクトに $type が付与されないため、
        // 期待される型に基づいてディスクリミネータを補完する。
        bool addedTypeDiscriminator = AddLegacyTypeDiscriminator(jsonObject, type);

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
            var exception = new InvalidCastException(
                $"Discriminator type '{actualType}' is not assignable to the expected type '{type}'.");
            if (FallbackDeserializationHelper.TryCreateFallback(
                    type,
                    actualType,
                    jsonObject,
                    exception) is { } incompatibleTypeFallback)
            {
                if (incompatibleTypeFallback is CoreObject coreObject)
                {
                    coreObject.Uri = uri;
                }

                return incompatibleTypeFallback;
            }

            throw exception;
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
            PopulateFromJsonObjectCore(obj, type, jsonObject, options, addedTypeDiscriminator);
            if (obj is CoreObject restoredCoreObject)
            {
                restoredCoreObject.WasTypeDiscriminatorAddedDuringRestore =
                    addedTypeDiscriminator;
            }

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
            if (fallback is CoreObject coreObject)
                coreObject.Uri = uri;
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
        ReferencedStorageCapture.Record(uri);

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();
        bool addedTypeDiscriminator = AddLegacyTypeDiscriminator(jsonObject, obj.GetType());
        if (obj is CoreObject coreObj)
        {
            coreObj.Uri = uri;
        }

        var options = new CoreSerializerOptions { BaseUri = uri, Mode = CoreSerializationMode.Read };
        PopulateFromJsonObjectCore(obj, type, jsonObject, options, addedTypeDiscriminator);
        if (obj is CoreObject populatedCoreObject)
        {
            populatedCoreObject.WasTypeDiscriminatorAddedDuringRestore =
                addedTypeDiscriminator;
        }
    }

    private static bool AddLegacyTypeDiscriminator(JsonObject json, Type type)
    {
        // Preserve present but invalid discriminators for unknown-type recovery.
        if (json.ContainsKey("$type") || json.ContainsKey("@type"))
        {
            return false;
        }

        if (type == typeof(ProjectItem) || type.FullName == "Beutl.ProjectSystem.Scene")
        {
            json["$type"] = LegacyTypeNames.SceneDiscriminator;
            return true;
        }

        if (type.FullName == LegacyTypeNames.ElementFullName)
        {
            json["$type"] = LegacyTypeNames.ElementDiscriminator;
            return true;
        }

        return false;
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
        // A project save writes the files it references while the project itself is still being
        // serialized, so its own bytes reach the disk last. Gate the destination first, the way the
        // scene save and the auto-save do, so the compatibility gate is never behind the sidecars it
        // guards. The gate is written to the URI this save names rather than the one the project
        // currently carries, which a first save does not have and a Save As points elsewhere. That
        // write omits SaveReferencedObjects, so it stops here rather than recursing.
        if (obj is Project project
            && uri.Scheme == "file"
            && (mode ?? DefaultStoreMode).HasFlag(CoreSerializationMode.SaveReferencedObjects))
        {
            PersistProjectMigrationGate(project, uri);
        }

        // Serialization is synchronous, like ThreadLocalSerializationContext. Track
        // object identity AND destination so back edges retain their URI without
        // re-entering a file that this call chain is already writing.
        var activeWrites = t_activeWrites ??= new(ReferenceEqualityComparer.Instance);
        ICoreSerializable identity = obj;
        if (!activeWrites.TryGetValue(identity, out HashSet<Uri>? destinations))
        {
            destinations = [];
            activeWrites.Add(identity, destinations);
        }
        if (!destinations.Add(uri)) return;

        try
        {
            StoreToUriImpl(obj, uri, mode, authorizedRootPath);
        }
        finally
        {
            // Only in-progress writes are suppressed. A later save may carry new
            // values, and failed saves must always be retryable.
            destinations.Remove(uri);
            if (destinations.Count == 0) activeWrites.Remove(identity);
            if (activeWrites.Count == 0) t_activeWrites = null;
        }
    }

    private static void StoreToUriImpl<T>(
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
                if (suppressed.WasReinstated)
                    CopyReferencedStorageSources(suppressed, uri, suppressed.SourceRootPath, restore: true);
                RestoreReinstatedBytes(suppressed, sourcePath);
                return;
            }

            if (suppressed.WasReinstated && uri == suppressedObj.Uri)
            {
                CopyReferencedStorageSources(suppressed, uri, authorizedRootPath, restore: true);
                RestoreReinstatedBytes(suppressed, uri.LocalPath);
                return;
            }

            if (uri.Scheme != "file")
            {
                throw new JsonException();
            }

            // Rehomed (save-as): the retained bytes move verbatim so the new project copy keeps the
            // element. SourceUri stays unchanged so the source location remains skip-protected if a
            // failed multi-file save rolls Uri back afterwards.
            string rehomedPath = uri.LocalPath;
            if (File.Exists(rehomedPath))
            {
                EnsureExistingBytesMatch(rehomedPath, suppressed.RawBytes);

                CopyReferencedStorageSources(suppressed, uri, authorizedRootPath);
                ClearReinstatement(suppressed);
                suppressedObj.Uri = uri;
                return;
            }

            CopyReferencedStorageSources(suppressed, uri, authorizedRootPath);

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
                    StorageWriteTransaction.MoveIntoPlace(tempPath, rehomedPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(rehomedPath))
                {
                    EnsureExistingBytesMatch(rehomedPath, suppressed.RawBytes);

                    ClearReinstatement(suppressed);
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

            ClearReinstatement(suppressed);
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
            var options = new CoreSerializerOptions { BaseUri = uri, Mode = mode ?? DefaultStoreMode };
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

                StorageWriteTransaction.MoveIntoPlace(
                    tmp,
                    path,
                    overwrite: true,
                    isCompatibilityGate: obj is Project);
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
        string? authorizedRootPath,
        bool restore = false)
    {
        if (suppressed.ReferencedStorageSources is not { Length: > 0 } referencedSources)
        {
            return;
        }

        if (suppressed.SourceRootPath is null)
        {
            throw new JsonException("Retained sidecars have no authorized source root.");
        }

        string destinationRoot = Path.TrimEndingDirectorySeparator(
            PathBoundary.ResolveDeepestExistingTarget(
                authorizedRootPath
                ?? Path.GetDirectoryName(rehomedUri.LocalPath)
                ?? throw new JsonException("Rehomed element has no destination directory.")));
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
            string resolvedDestination = PathBoundary.ResolveDeepestExistingTarget(destination);
            if (!PathBoundary.IsPathInsideRoot(destinationRoot, resolvedDestination))
            {
                throw new JsonException($"Retained sidecar escapes the Save As root: {relativePath}");
            }

            copies.Add((source, destination));
        }

        foreach ((SuppressedReferencedStorageSource source, string destination) in copies)
        {
            if (!restore && File.Exists(destination))
            {
                EnsureExistingBytesMatch(destination, source.RawBytes);
            }
        }

        foreach ((SuppressedReferencedStorageSource source, string destination) in copies)
        {
            if (restore)
            {
                // Undo restores files written by the completed repair as well as missing files.
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                WriteBytesAtomically(destination, source.RawBytes);
            }
            else
            {
                WriteBytesAtomicallyIfMatchingOrMissing(destination, source.RawBytes);
            }
        }
    }

    private static void WriteBytesAtomicallyIfMatchingOrMissing(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            EnsureExistingBytesMatch(path, bytes);
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
                StorageWriteTransaction.MoveIntoPlace(tempPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                EnsureExistingBytesMatch(path, bytes);
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

    private static void EnsureExistingBytesMatch(string path, byte[] expectedBytes)
    {
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedBytes))
        {
            throw new IOException($"The retained sidecar destination already contains different data: '{path}'.");
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

        ClearReinstatement(suppressed);
    }

    private static void ClearReinstatement(SuppressedStorageSource suppressed)
    {
        if (!suppressed.WasReinstated)
        {
            return;
        }

        suppressed.WasReinstated = false;
        // A rolled-back save puts the replaced bytes back, so the record must still restore them.
        StorageWriteTransaction.Current?.OnRollback(() => suppressed.WasReinstated = true);
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

            StorageWriteTransaction.MoveIntoPlace(tempPath, path, overwrite: true);
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
