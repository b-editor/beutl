using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Serialization;

public static partial class CoreSerializer
{
    private const string RetainedBytesMember = "$storageBytes";
    private static readonly ConditionalWeakTable<CoreObject, OwnedStorageState> s_ownedStorage = new();
    private static ILogger? s_storageLogger;
    private static ILogger StorageLogger => Log.GetLoggerOnceConfigured(ref s_storageLogger, typeof(CoreSerializer));

    internal static T? ReadOptionalReference<T>(ICoreSerializationContext context, string name)
        where T : CoreObject
    {
        try
        {
            return context.GetValue<T>(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            StorageLogger.LogWarning(ex,
                "Could not read optional reference {Name} in {Uri}; continuing without it may reduce repair information.",
                name, context.BaseUri);
            return null;
        }
    }

    private static bool IsLaterStorageVersion(ICoreSerializable obj, JsonObject json)
        => obj is IStorageVersioned versioned
           && json[versioned.VersionMember] is JsonValue value
           && value.GetValueKind() == JsonValueKind.Number
           && long.TryParse(value.ToJsonString(), out long version)
           && version > versioned.SupportedVersion;

    private static bool RetainLaterVersion(ICoreSerializable obj, JsonObject json, Uri? uri, Func<byte[]> readBytes)
    {
        if (obj is not CoreObject coreObject || !IsLaterStorageVersion(obj, json)) return false;
        byte[] bytes = readBytes();
        Uri source = uri ?? UriHelper.CreateBase64DataUri("application/json", bytes);
        TakeVersionFields(ReadRetainedObject(bytes), source);
        coreObject.SuppressedStorageSource = new SuppressedStorageSource(bytes, source);
        StorageLogger.LogWarning("Keeping the later schema in {Uri} without interpreting or replacing it.", uri);
        return true;
    }

    private static JsonObject ReadRetainedObject(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return JsonNode.Parse(stream) as JsonObject ?? throw new JsonException("Retained storage is not an object.");
    }

    private static bool RetainEmbeddedLaterVersion(ICoreSerializable obj, JsonObject json)
        => RetainLaterVersion(obj, json, (obj as CoreObject)?.Uri, () =>
        {
            if (json[RetainedBytesMember] is JsonValue value && value.TryGetValue(out string? encoded))
            {
                try { return Convert.FromBase64String(encoded); }
                catch (FormatException ex) { throw new JsonException("Invalid retained storage bytes.", ex); }
            }

            JsonObject retained = (JsonObject)json.DeepClone();
            retained.Remove("Uri");
            return JsonSerializer.SerializeToUtf8Bytes(retained, JsonHelper.SerializerOptions);
        });

    private static void RetainLaterVersionOnDisk(CoreObject obj, Uri uri)
    {
        if (obj is not IStorageVersioned || obj.SuppressedStorageSource is not null || !uri.IsFile) return;
        try
        {
            byte[] bytes = File.ReadAllBytes(uri.LocalPath);
            JsonObject json = ReadRetainedObject(bytes);
            TakeVersionFields(json, uri);
            RetainLaterVersion(obj, json, uri, () => bytes);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (JsonException) { }
    }

    internal static void TrackOwnedReference(CoreObject owner, CoreObject? value, Uri? ownedUri)
    {
        if (owner.Uri is not { IsFile: true } ownerUri || ownedUri is not { IsFile: true }
            || value?.Uri is not { IsFile: true } valueUri
            || !FilePathComparison.AreSameCanonicalPath(valueUri.LocalPath, ownedUri.LocalPath)) return;
        s_ownedStorage.GetValue(owner, _ => new OwnedStorageState(ownerUri, [])).Files.Add(new OwnedStorageFile(valueUri, value));
    }

    internal static void WriteOwnedReference<T>(
        CoreObject owner, ICoreSerializationContext context, string name, T? value, Uri? ownedUri)
        where T : CoreObject, new()
    {
        // The caller declares an exclusive file; borrowed references use context.SetValue instead.
        if (ownedUri is null) return;
        OwnedStorageWrite? write = OwnedStorageWrite.Current;
        bool storingOwner = ReferenceEquals(write?.Owner, owner);
        if (storingOwner)
        {
            T candidate = value ?? new T();
            RetainLaterVersionOnDisk(candidate, ownedUri);
            if (candidate.SuppressedStorageSource is not null) value = candidate;
        }

        if (value is null) return;
        value.Uri = ownedUri;
        context.SetValue(name, value);
        if (storingOwner) write!.Files.Add(new OwnedStorageFile(ownedUri, value));
    }

    private sealed record OwnedStorageFile(Uri Uri, CoreObject Value);

    private sealed record OwnedStorageState(Uri OwnerUri, List<OwnedStorageFile> Files);

    private sealed class OwnedStorageWrite : IDisposable
    {
        [ThreadStatic]
        private static OwnedStorageWrite? t_current;
        private readonly OwnedStorageWrite? _parent;
        private readonly Uri _destination;

        public OwnedStorageWrite(ICoreSerializable owner, Uri destination)
        {
            Owner = owner;
            _destination = destination;
            _parent = t_current;
            t_current = this;
        }

        public static OwnedStorageWrite? Current => t_current;
        public ICoreSerializable Owner { get; }
        public List<OwnedStorageFile> Files { get; } = [];

        public void Complete()
        {
            if (Owner is not CoreObject coreObject) return;
            s_ownedStorage.TryGetValue(coreObject, out OwnedStorageState? previous);
            if (previous is null && Files.Count == 0) return;
            if (previous is not null && _destination.IsFile && previous.OwnerUri.IsFile
                && FilePathComparison.AreSameCanonicalPath(previous.OwnerUri.LocalPath, _destination.LocalPath))
            {
                string root = Path.GetDirectoryName(_destination.LocalPath)!;
                foreach (OwnedStorageFile dropped in previous.Files)
                {
                    Uri uri = dropped.Uri;
                    if (!uri.IsFile || dropped.Value.SuppressedStorageSource is not null
                        || dropped.Value.Uri != uri
                        || !FilePathComparison.IsSameOrDescendant(root, uri.LocalPath)
                        || Files.Any(file => file.Uri is { IsFile: true } current
                                             && FilePathComparison.AreSameCanonicalPath(current.LocalPath, uri.LocalPath))) continue;
                    RetainLaterVersionOnDisk(dropped.Value, uri);
                    if (dropped.Value.SuppressedStorageSource is null) StorageWriteTransaction.Delete(uri.LocalPath);
                }
            }

            s_ownedStorage.Remove(coreObject);
            s_ownedStorage.Add(coreObject, new OwnedStorageState(_destination, Files));
            StorageWriteTransaction.Current?.OnRollback(() =>
            {
                s_ownedStorage.Remove(coreObject);
                if (previous is not null) s_ownedStorage.Add(coreObject, previous);
            });
        }

        public void Dispose() => t_current = _parent;
    }
}

internal interface IStorageVersioned
{
    string VersionMember { get; }
    long SupportedVersion { get; }
}
