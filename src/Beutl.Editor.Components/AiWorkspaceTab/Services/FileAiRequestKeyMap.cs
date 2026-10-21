using System.Reactive.Disposables;
using System.Text.Json;
using Beutl.Api.Services;
using Beutl.Language;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

// The key a metered request went out under while its outcome is unknown; its job is looked up by it.
internal sealed record AiPendingAttempt(
    string AccountId,
    string Operation,
    string Fingerprint,
    string Key,
    string? Model = null)
{
    public string DisplayName
    {
        get
        {
            // Only the image and video dialogs keep pending attempts.
            string operation = Operation switch
            {
                "image.generate" => Strings.AiImageGeneration,
                "video.generate" => Strings.AiVideoGeneration,
                "video.edit" => Strings.AiVideoEditing,
                "video.extend" => Strings.AiVideoExtend,
                "video.motion" => Strings.AiVideoMotion,
                _ when Operation.StartsWith("image.edit.", StringComparison.Ordinal)
                    => Strings.AiImageEdit,
                _ => Operation,
            };
            return Model is { Length: > 0 }
                ? $"{operation} ({Model})"
                : string.Format(Strings.AiPendingRequestDefaultModel, operation);
        }
    }
}

// Fingerprint-to-key map per account and operation. Rows keep the shape of the first key-only
// store, so older versions read them; a row this version cannot read is skipped.
internal sealed class FileAiRequestKeyMap
{
    private const int Version = 1;
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumRecords = 256;
    private const int MaximumTextLength = 4_096;

    private readonly Lock _gate = new();
    private readonly string _path;

    public FileAiRequestKeyMap(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        string directory = Path.GetFullPath(storageDirectory);
        Directory.CreateDirectory(directory);
        // The keys name paid requests of this account, so only the user may read them.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _path = Path.Combine(directory, "ai-request-recovery.json");
        DeleteRetiredFiles(directory);
    }

    internal string StoragePath => _path;

    // How long a read or write waits for another Beutl process to release the map.
    internal TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public (AiPendingAttempt Attempt, bool Existed) GetOrAdd(
        string accountId,
        string operation,
        string fingerprint,
        string? model,
        Func<string> createKey)
    {
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            if (records.Find(record => IsSame(record, accountId, operation, fingerprint)) is { } existing)
                return (existing, true);
            if (records.Count >= MaximumRecords)
                throw new InvalidDataException("AI request recovery is full; abandon a pending request first.");

            var added = new AiPendingAttempt(accountId, operation, fingerprint, createKey(), model);
            records.Add(added);
            Save(records);
            return (added, false);
        }
    }

    // Removes the row only while it still holds attempt's key.
    public bool Remove(AiPendingAttempt attempt)
    {
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            int removed = records.RemoveAll(record => IsSame(record, attempt.AccountId, attempt.Operation, attempt.Fingerprint)
                && StringComparer.Ordinal.Equals(record.Key, attempt.Key));
            if (removed != 0)
                Save(records);
            return removed != 0;
        }
    }

    // The rows of operation and of the operations under it, such as image.edit.upscale under image.edit.
    public IReadOnlyList<AiPendingAttempt> PendingFor(string accountId, string operation)
    {
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            return Load()
                .Where(record => record.AccountId == accountId
                    && (record.Operation == operation
                        || record.Operation.StartsWith(operation + ".", StringComparison.Ordinal)))
                .ToArray();
        }
    }

    public bool HasAny(string accountId, string operation) => PendingFor(accountId, operation).Count != 0;

    private static bool IsSame(AiPendingAttempt record, string accountId, string operation, string fingerprint)
        => record.AccountId == accountId && record.Operation == operation && record.Fingerprint == fingerprint;

    private FileStream AcquireLock()
    {
        long deadline = Environment.TickCount64 + (long)LockTimeout.TotalMilliseconds;
        while (true)
        {
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                return new FileStream(_path + ".lock", options);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                // Another Beutl process holds the map for one short read or write.
                Thread.Sleep(20);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException("AI request recovery is unavailable.", ex);
            }
        }
    }

    private List<AiPendingAttempt> Load()
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length is <= 0 or > MaximumBytes)
                return [];
            bytes = File.ReadAllBytes(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("AI request recovery is unreadable.", ex);
        }

        var records = new List<AiPendingAttempt>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int number)
                || number != Version
                || !root.TryGetProperty("records", out JsonElement items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return records;
            }

            // The Form and Sources an older version kept are ignored; the lookup needs only the key.
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (TryRead(item) is { } record
                    && records.Count < MaximumRecords
                    && !records.Exists(other => IsSame(other, record.AccountId, record.Operation, record.Fingerprint)))
                {
                    records.Add(record);
                }
            }
        }
        catch (JsonException)
        {
            // Left as it is until the next write replaces it.
        }

        return records;
    }

    private static AiPendingAttempt? TryRead(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Text(item, nameof(AiPendingAttempt.AccountId)) is not { } account
            || Text(item, nameof(AiPendingAttempt.Operation)) is not { } operation
            || Text(item, nameof(AiPendingAttempt.Fingerprint)) is not { } fingerprint
            || Text(item, nameof(AiPendingAttempt.Key)) is not { } key
            || !IsIdempotencyKey(key))
        {
            return null;
        }

        string? model = null;
        if (item.TryGetProperty(nameof(AiPendingAttempt.Model), out JsonElement modelElement)
            && modelElement.ValueKind != JsonValueKind.Null)
        {
            model = Text(item, nameof(AiPendingAttempt.Model));
            if (model is null)
                return null;
        }

        return new AiPendingAttempt(account, operation, fingerprint, key, model);
    }

    private static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= MaximumTextLength } text
            && !text.Any(char.IsControl)
                ? text
                : null;

    private static bool IsIdempotencyKey(string key)
        => key.Length <= 255 && key.All(character => character is >= '\x21' and <= '\x7e');

    private void Save(List<AiPendingAttempt> records)
    {
        try
        {
            JsonHelper.WriteAtomically(
                _path,
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", Version);
                    writer.WriteStartArray("records");
                    foreach (AiPendingAttempt record in records)
                    {
                        writer.WriteStartObject();
                        writer.WriteString(nameof(AiPendingAttempt.AccountId), record.AccountId);
                        writer.WriteString(nameof(AiPendingAttempt.Operation), record.Operation);
                        writer.WriteString(nameof(AiPendingAttempt.Fingerprint), record.Fingerprint);
                        writer.WriteString(nameof(AiPendingAttempt.Key), record.Key);
                        writer.WriteString(nameof(AiPendingAttempt.Model), record.Model);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                },
                journal: false,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("AI request recovery could not be saved.", ex);
        }
    }

    // What the replaced recovery and retry stores kept beside their keys; nothing reads it now.
    private static void DeleteRetiredFiles(string directory)
    {
        try
        {
            foreach (string name in new[]
                     {
                         "ai-request-recovery-generations.json", "ai-request-recovery-claims.json",
                         "retry-keys.json", "retry-keys.json.lock",
                     })
            {
                File.Delete(Path.Combine(directory, name));
            }

            foreach (string pattern in new[] { "ai-request-recovery-*.json.*.tmp", "retry-keys.json.*.tmp" })
            {
                foreach (string path in Directory.EnumerateFiles(directory, pattern))
                    File.Delete(path);
            }

            string sources = Path.Combine(directory, "ai-request-recovery-sources");
            if (Directory.Exists(sources))
                Directory.Delete(sources, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start.
        }
    }
}

internal sealed class AiRequestRecoveryContext : IDisposable
{
    private readonly FileAiRequestKeyMap _store;
    private readonly Func<AiAuthenticatedRequestIdentity?> _identityProvider;
    private readonly IDisposable? _identitySubscription;
    private string? _lastAccount;

    public event Action? IdentityChanged;

    public AiRequestRecoveryContext(
        FileAiRequestKeyMap store,
        Func<AiAuthenticatedRequestIdentity?> identityProvider,
        IObservable<AiAuthenticatedRequestIdentity?>? identityChanges = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _identitySubscription = identityChanges?.Subscribe(_ => RefreshIdentity());
    }

    public FileAiRequestKeyMap Store => _store;

    public AiAuthenticatedRequestIdentity GetRequiredIdentity()
    {
        AiAuthenticatedRequestIdentity identity = _identityProvider()
            ?? throw new AuthenticationRequiredException();
        if (IsInconsistent(identity))
        {
            throw new AuthenticationRequiredException();
        }

        return identity;
    }

    public AiAuthenticatedRequestIdentity? TryGetIdentity()
    {
        AiAuthenticatedRequestIdentity? current = _identityProvider();
        if (current is { } identity && IsInconsistent(identity))
        {
            current = null;
        }

        string? account = current?.AccountId;
        if (!StringComparer.Ordinal.Equals(account, _lastAccount))
        {
            _lastAccount = account;
            try
            {
                foreach (Action handler in IdentityChanged?.GetInvocationList().Cast<Action>()
                    ?? Array.Empty<Action>())
                {
                    try
                    {
                        handler();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        return current;
    }

    public void RefreshIdentity() => _ = TryGetIdentity();

    private static bool IsInconsistent(AiAuthenticatedRequestIdentity identity)
        => string.IsNullOrWhiteSpace(identity.AccountId)
            || identity.User is { } user
                && !StringComparer.Ordinal.Equals(user.Profile.Id, identity.AccountId);

    public IReadOnlyList<AiPendingAttempt> PendingFor(string operation)
        => TryGetIdentity() is { } identity
            ? _store.PendingFor(identity.AccountId, operation)
            : Array.Empty<AiPendingAttempt>();

    public bool Abandon(AiPendingAttempt attempt)
    {
        AiAuthenticatedRequestIdentity identity = GetRequiredIdentity();
        if (!StringComparer.Ordinal.Equals(identity.AccountId, attempt.AccountId))
            throw new AuthenticationRequiredException();
        return _store.Remove(attempt);
    }

    public void Dispose() => _identitySubscription?.Dispose();

    public IDisposable Enter(string issuedAccountId)
    {
        AiAuthenticatedRequestIdentity current = GetRequiredIdentity();
        if (!StringComparer.Ordinal.Equals(current.AccountId, issuedAccountId))
            throw new AuthenticationRequiredException();
        return current.User is null
            ? Disposable.Empty
            : AiAuthenticatedRequestScope.Enter(current.User);
    }
}
