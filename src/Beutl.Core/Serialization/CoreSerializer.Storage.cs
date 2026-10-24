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

    internal static bool StoreToUriIfChanged<T>(T obj, Uri uri, CoreSerializationMode? mode = null)
        where T : ICoreSerializable
        => StoreToUriCore(obj, uri, mode, authorizedRootPath: null);

    private static bool StoreToUriCore<T>(
        T obj,
        Uri uri,
        CoreSerializationMode? mode,
        string? authorizedRootPath)
        where T : ICoreSerializable
    {
        CoreObject? coreObject = obj as CoreObject;
        Uri? previousUri = coreObject?.Uri;
        using var transaction = StorageWriteTransaction.Current is null ? StorageWriteTransaction.Begin() : null;
        try
        {
            bool changed = StoreToUriWithTracking(obj, uri, mode, authorizedRootPath);
            transaction?.Commit();
            return changed;
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

    private static bool StoreToUriWithTracking<T>(
        T obj,
        Uri uri,
        CoreSerializationMode? mode,
        string? authorizedRootPath)
        where T : ICoreSerializable
    {
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
        if (!destinations.Add(uri)) return false;

        try
        {
            return StoreToUriImpl(obj, uri, mode, authorizedRootPath);
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

    private static bool StoreToUriImpl<T>(
        T obj,
        Uri uri,
        CoreSerializationMode? mode,
        string? authorizedRootPath)
        where T : ICoreSerializable
    {
        // Released builds read only the project file's version fields, so it is raised before this file is
        // written. A project's own file needs no raise: the files it references are written while it is
        // serialized, and each of them raises it first.
        if (obj is not Project && FindProject(obj) is { } project)
        {
            RaiseProjectFile(project, GetMinAppVersionToWrite(obj));
        }

        if (obj is CoreObject versionedObject)
            RetainLaterVersionOnDisk(versionedObject, uri);

        if (obj is CoreObject { SuppressedStorageSource: { } suppressed } suppressedObj)
        {
            return StoreSuppressedSource(suppressed, suppressedObj, uri, authorizedRootPath);
        }

        if (uri.Scheme == "file")
        {
            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }

            CoreSerializationMode storeMode = mode ?? DefaultStoreMode;
            var options = new CoreSerializerOptions { BaseUri = uri, Mode = storeMode };
            string? minAppVersion = null;
            using var ownedFiles = new OwnedStorageWrite(obj, uri);
            bool changed = WriteJsonAtomically(
                uri.LocalPath,
                isCompatibilityGate: obj is Project,
                writer =>
                {
                    JsonObject json = SerializeToJsonObject(obj, options);
                    if (IsRootFile(obj))
                    {
                        // Read after serializing: a project records what the files it wrote meanwhile require.
                        minAppVersion = GetMinAppVersionToWrite(obj);
                        // Only a project file records the version that saved it: a scene or element file that
                        // a newer version saves unchanged stays byte for byte as it was.
                        WriteVersionFields(json, obj is Project ? BeutlApplication.Version : null, minAppVersion);
                    }

                    json.WriteTo(writer, JsonHelper.SerializerOptions);
                });
            ownedFiles.Complete();
            if (minAppVersion is not null)
            {
                RootFileVersions.RaiseMinAppVersion(obj, minAppVersion);
                if (obj is Project savedProject)
                {
                    RootFileVersions.RecordAppVersion(savedProject, BeutlApplication.Version);
                    RememberProjectFile(savedProject, uri.LocalPath, minAppVersion);
                }
            }

            // Saved with its files, as an explicit save does; an auto-save writes the file alone after every edit.
            if (obj is Project projectWithFiles && storeMode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
            {
                DuplicateIds.Report(projectWithFiles, "save");
            }

            return changed;
        }
        else
        {
            throw new JsonException();
        }
    }

    // A recovered object keeps its retained bytes on disk: saving it restores or rehomes them verbatim
    // instead of serializing it.
    private static bool StoreSuppressedSource(
        SuppressedStorageSource suppressed,
        CoreObject suppressedObj,
        Uri uri,
        string? authorizedRootPath)
    {
        if (uri == suppressed.SourceUri)
        {
            // An undone repair (WasReinstated) writes the retained bytes back, so the next open sees what the undo
            // restored. A record held all along leaves a changed file alone: that is the user's repair.
            string sourcePath = uri.LocalPath;
            if (suppressed.WasReinstated)
                CopyReferencedStorageSources(suppressed, uri, suppressed.SourceRootPath, restore: true);
            return RestoreReinstatedBytes(suppressed, sourcePath);
        }

        if (suppressed.WasReinstated && uri == suppressedObj.Uri)
        {
            CopyReferencedStorageSources(suppressed, uri, authorizedRootPath, restore: true);
            return RestoreReinstatedBytes(suppressed, uri.LocalPath);
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
        }

        CopyReferencedStorageSources(suppressed, uri, authorizedRootPath);
        bool changed = WriteBytesAtomicallyIfMatchingOrMissing(rehomedPath, suppressed.RawBytes);
        ClearReinstatement(suppressed);
        suppressedObj.Uri = uri;
        return changed;
    }

    // Writes a project, scene or element file atomically, recorded in the active storage write transaction.
    private static bool WriteJsonAtomically(string path, bool isCompatibilityGate, Action<Utf8JsonWriter> write)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        return JsonHelper.WriteAtomically(path, write, journal: true, isCompatibilityGate: isCompatibilityGate);
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
            FilePathComparison.ResolveCanonicalPath(
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
            string resolvedDestination = FilePathComparison.ResolveCanonicalPath(destination);
            if (!FilePathComparison.IsSameOrDescendant(destinationRoot, resolvedDestination))
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

    private static bool WriteBytesAtomicallyIfMatchingOrMissing(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            EnsureExistingBytesMatch(path, bytes);
            return false;
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
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                EnsureExistingBytesMatch(path, bytes);
                return false;
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

    private static bool RestoreReinstatedBytes(SuppressedStorageSource suppressed, string path)
    {
        if (!suppressed.WasReinstated && File.Exists(path))
        {
            return false;
        }

        bool changed = !File.Exists(path)
                       || !File.ReadAllBytes(path).AsSpan().SequenceEqual(suppressed.RawBytes);
        if (changed)
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            WriteBytesAtomically(path, suppressed.RawBytes);
        }

        ClearReinstatement(suppressed);
        return changed;
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
