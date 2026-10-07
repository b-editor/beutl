using System.Text.Json;

using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public static partial class CoreSerializer
{
    [ThreadStatic]
    private static Dictionary<ICoreSerializable, HashSet<Uri>>? t_activeWrites;

    // What StoreToUri writes with when its caller names no mode.
    private const CoreSerializationMode DefaultStoreMode =
        CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects;

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
        CoreObject? coreObject = obj as CoreObject;
        Uri? previousUri = coreObject?.Uri;
        try
        {
            StoreToUriWithTracking(obj, uri, mode, authorizedRootPath);
        }
        catch
        {
            // References need the destination during serialization, but a failed save must not
            // redirect subsequent auto-saves away from the object's previous storage identity.
            if (coreObject is not null)
                coreObject.Uri = previousUri;
            throw;
        }
    }

    private static void StoreToUriWithTracking<T>(
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
            StoreSuppressedSource(suppressed, suppressedObj, uri, authorizedRootPath);
            return;
        }

        if (uri.Scheme == "file")
        {
            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }

            var options = new CoreSerializerOptions { BaseUri = uri, Mode = mode ?? DefaultStoreMode };
            WriteJsonAtomically(
                uri.LocalPath,
                isCompatibilityGate: obj is Project,
                writer => SerializeToJsonObject(obj, options)
                    .WriteTo(writer, JsonHelper.SerializerOptions));
        }
        else
        {
            throw new JsonException();
        }
    }

    // A recovered object keeps its retained bytes on disk: saving it restores or rehomes them verbatim
    // instead of serializing it.
    private static void StoreSuppressedSource(
        SuppressedStorageSource suppressed,
        CoreObject suppressedObj,
        Uri uri,
        string? authorizedRootPath)
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
            using (var stream = StorageWriteTransaction.CreateTemporaryFile(tempPath, rehomedPath))
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
    }

    // tmp に書き出してから rename する。書き込み中のクラッシュや電源断で
    // 既存のプロジェクトファイル / Element ファイルがゼロバイト化したり
    // 中途半端な状態で残るのを防ぐ。
    // 固定 `.tmp` サフィックスだとユーザーや他ツールが既に持つ同名ファイルを
    // 上書きしてしまうため、ランダムサフィックスを付与して衝突を避ける。
    internal static bool UpdateStoredMetadata(Uri uri, Guid objectId, string name, JsonNode value)
    {
        if (!uri.IsFile || !File.Exists(uri.LocalPath)) return false;
        JsonObject json;
        using (var stream = UriHelper.ResolveStream(uri)) json = ParseStoredObject(stream, uri);
        if (!Guid.TryParse(json[nameof(CoreObject.Id)]?.GetValue<string>(), out var id) || id != objectId)
            return false;
        json[name] = value;
        WriteJsonAtomically(uri.LocalPath, false, writer => json.WriteTo(writer, JsonHelper.SerializerOptions));
        return true;
    }

    private static void WriteJsonAtomically(string path, bool isCompatibilityGate, Action<Utf8JsonWriter> write)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = StorageWriteTransaction.CreateTemporaryFile(temporaryPath, path))
            using (var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions))
            {
                write(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            StorageWriteTransaction.MoveIntoPlace(
                temporaryPath,
                path,
                overwrite: true,
                isCompatibilityGate: isCompatibilityGate);
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
            using (var stream = StorageWriteTransaction.CreateTemporaryFile(tempPath, path))
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
            using (var stream = StorageWriteTransaction.CreateTemporaryFile(tempPath, path))
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
