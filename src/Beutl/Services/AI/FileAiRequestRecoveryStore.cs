using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Api.Objects;
using Beutl.Api.Services;

namespace Beutl.Services.AI;

/// <summary>
/// A source referenced by a pending paid request. The row contains only a
/// locator and a content stamp; the content itself is kept in a private
/// recovery copy only when the original locator is process-local.
/// </summary>
internal sealed record AiRequestRecoverySource(
    string Role,
    string? Path,
    string Name,
    string ContentHash,
    long Length,
    string? DurableFile = null,
    string? ElementId = null)
{
    [JsonIgnore]
    public bool IsDurable => !string.IsNullOrEmpty(DurableFile);
}

/// <summary>
/// Canonical form state needed to rebuild one request after a process restart.
/// Nullable members are intentional: each form uses a different subset, while
/// an explicit null model is represented by <see cref="AiPendingAttempt.Model"/>
/// rather than by an absent recovery row.
/// </summary>
internal sealed record AiRequestFormSnapshot(
    string? Prompt = null,
    string? Style = null,
    string? Composition = null,
    string? Motion = null,
    string? Exclusions = null,
    string? Task = null,
    string? AspectRatio = null,
    string? Background = null,
    int? Seed = null,
    int? DurationSeconds = null,
    string? Resolution = null,
    bool? GenerateAudio = null,
    int? OutpaintExpansionPercent = null,
    int? MaxReferenceImages = null,
    long? MaxReferenceTotalBytes = null,
    bool? SupportsReferenceImage = null,
    bool? SupportsSeed = null,
    bool? HasBackgroundChoice = null,
    bool? SupportsAudio = null,
    bool? SupportsFirstFrame = null,
    bool? SupportsLastFrame = null,
    string? SourceName = null,
    bool? SourceIsPrepared = null,
    string? SourceElementId = null,
    string? FirstFrameElementId = null,
    string? LastFrameElementId = null);

/// <summary>
/// The complete durable identity of a request whose server outcome is not yet
/// known. This is deliberately an internal domain record: API request DTOs do
/// not own recovery state and therefore cannot accidentally serialize it.
/// </summary>
internal sealed record AiPendingAttempt(
    string AccountId,
    string Operation,
    string Fingerprint,
    string Key,
    string? Model = null,
    AiRequestFormSnapshot? Form = null,
    IReadOnlyList<AiRequestRecoverySource>? Sources = null)
{
    [JsonIgnore]
    public IReadOnlyList<AiRequestRecoverySource> EffectiveSources
        => Sources ?? Array.Empty<AiRequestRecoverySource>();

    [JsonIgnore]
    public bool HasCanonicalForm => Form is not null;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            string operation = Operation switch
            {
                "image.generate" => "Image generation",
                "video.generate" => "Video generation",
                _ when Operation.StartsWith("image.edit.", StringComparison.Ordinal)
                    => "Image edit",
                _ => "AI request",
            };
            return Model is { Length: > 0 }
                ? $"{operation} ({Model})"
                : $"{operation} (default model)";
        }
    }
}

internal sealed class AiRequestRecoveryLease : IDisposable
{
    private readonly FileAiRequestRecoveryStore _store;
    private int _released;
    private bool _dispatched;

    internal AiRequestRecoveryLease(
        FileAiRequestRecoveryStore store,
        string accountId,
        string operation,
        string fingerprint,
        string key,
        int generation,
        string ownerToken)
    {
        _store = store;
        AccountId = accountId;
        Operation = operation;
        Fingerprint = fingerprint;
        Key = key;
        Generation = generation;
        OwnerToken = ownerToken;
    }

    internal string AccountId { get; }

    internal string Operation { get; }

    internal string Fingerprint { get; }

    internal string Key { get; }

    internal int Generation { get; }

    internal string OwnerToken { get; }

    internal bool IsDispatched => _dispatched;

    internal void MarkDispatched() => _dispatched = true;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _store.ReleaseClaim(this, force: false);
    }
}

/// <summary>Atomic, bounded local storage for unresolved metered AI attempts.</summary>
internal sealed class FileAiRequestRecoveryStore
{
    // Keep the version at one so rows written by the previous key-only store
    // remain readable. They are intentionally marked as lacking a form and can
    // never be silently replayed as a newly shaped request.
    private const int Version = 1;
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumRecords = 256;
    private const int MaximumSourcesPerRecord = 16;
    private const int MaximumPromptLength = 32_000;
    private const int MaximumScalarLength = 4_096;
    private const int MaximumPathLength = 4_096;
    private const long MaximumSourceBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan OrphanSourceAge = TimeSpan.FromHours(1);
    private const string SourceDirectoryName = "ai-request-recovery-sources";
    private const int MaximumGenerationEntries = 1_024;
    private const int MaximumClaims = 256;
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly HashSet<string> _newSourceFiles = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly string _sourceDirectory;
    private readonly string _generationPath;
    private readonly string _claimPath;
    private string LockPath => _path + ".lock";

    public FileAiRequestRecoveryStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        string fullDirectory = Path.GetFullPath(storageDirectory);
        Directory.CreateDirectory(fullDirectory);
        RestrictDirectory(fullDirectory);
        _path = Path.Combine(fullDirectory, "ai-request-recovery.json");
        _sourceDirectory = Path.Combine(fullDirectory, SourceDirectoryName);
        _generationPath = Path.Combine(fullDirectory, "ai-request-recovery-generations.json");
        _claimPath = Path.Combine(fullDirectory, "ai-request-recovery-claims.json");
        Directory.CreateDirectory(_sourceDirectory);
        RestrictDirectory(_sourceDirectory);
        SweepOrphanedSources();
    }

    internal string StoragePath => _path;

    internal string SourceDirectory => _sourceDirectory;

    internal int GetGeneration(string accountId, string operation, string fingerprint)
    {
        ValidateIdentity(accountId, operation, fingerprint);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            return LoadGenerations().FirstOrDefault(entry =>
                entry.AccountId == accountId
                && entry.Operation == operation
                && entry.Fingerprint == fingerprint)?.Generation ?? 0;
        }
    }

    internal int AdvanceGeneration(string accountId, string operation, string fingerprint)
    {
        ValidateIdentity(accountId, operation, fingerprint);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            return AdvanceGenerationCore(accountId, operation, fingerprint);
        }
    }

    internal static AiRequestRecoverySource CreateExternalSource(
        string role,
        string path,
        string name,
        ReadOnlySpan<byte> content,
        string? elementId = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A source path is required.", nameof(path));
        return new AiRequestRecoverySource(
            role,
            path,
            name,
            Convert.ToHexString(SHA256.HashData(content)),
            content.Length,
            DurableFile: null,
            elementId);
    }

    internal AiPendingAttempt? Find(string accountId, string operation, string fingerprint)
    {
        lock (_gate)
        {
            ValidateIdentity(accountId, operation, fingerprint);
            using FileStream lease = AcquireLock();
            return Load().FirstOrDefault(x => x.AccountId == accountId
                && x.Operation == operation
                && x.Fingerprint == fingerprint);
        }
    }

    internal bool TryUpdateForm(
        string accountId,
        string operation,
        string fingerprint,
        string key,
        AiRequestFormSnapshot form,
        IReadOnlyList<AiRequestRecoverySource>? sources)
    {
        lock (_gate)
        {
            ValidateIdentity(accountId, operation, fingerprint);
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            int index = records.FindIndex(attempt => attempt.AccountId == accountId
                && attempt.Operation == operation
                && attempt.Fingerprint == fingerprint
                && attempt.Key == key);
            if (index < 0)
                return false;
            records[index] = records[index] with { Form = form, Sources = sources ?? Array.Empty<AiRequestRecoverySource>() };
            ValidateRecord(records[index]);
            Save(records);
            MarkSourcesCommitted(records[index].EffectiveSources);
            return true;
        }
    }

    internal IReadOnlyList<AiPendingAttempt> PendingFor(string accountId, string operation)
    {
        ValidateText(accountId, 128, nameof(accountId));
        ValidateText(operation, 128, nameof(operation));
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

    internal AiPendingAttempt WriteOrGet(AiPendingAttempt attempt)
    {
        lock (_gate)
        {
            // Keep the on-disk shape unambiguous. A missing source list means
            // "no sources", never "the parser could not tell"; this also
            // makes a form without sources round-trip through the strict
            // current schema.
            attempt = attempt with { Sources = attempt.Sources ?? Array.Empty<AiRequestRecoverySource>() };
            ValidateRecord(attempt);
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            int existingIndex = records.FindIndex(x => x.AccountId == attempt.AccountId
                && x.Operation == attempt.Operation
                && x.Fingerprint == attempt.Fingerprint);
            if (existingIndex >= 0)
            {
                AiPendingAttempt existing = records[existingIndex];
                // The first key is authoritative. A later call may fill in a
                // form snapshot after an older key-only row, but can never
                // replace a durable identity with another key.
                if (!existing.HasCanonicalForm && attempt.HasCanonicalForm)
                {
                    records[existingIndex] = attempt with { Key = existing.Key };
                    Save(records);
                    MarkSourcesCommitted(attempt.EffectiveSources);
                    return records[existingIndex];
                }

                DeleteUncommittedSourcesCore(attempt.EffectiveSources, records);

                return existing;
            }

            if (records.Count >= MaximumRecords)
                throw new InvalidDataException(
                    "AI request recovery store is full; unresolved attempts were retained.");

            records.Add(attempt);
            Save(records);
            foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
            {
                if (source.DurableFile is { } durable)
                    _newSourceFiles.Remove(durable);
            }
            return attempt;
        }
    }

    internal AiRequestRecoveryLease Claim(
        string accountId,
        string operation,
        string fingerprint,
        string key)
    {
        ValidateIdentity(accountId, operation, fingerprint);
        if (!IsPrintable(key, 255))
            throw new InvalidDataException("AI request recovery key is invalid.");
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            AiPendingAttempt? current = Load().FirstOrDefault(attempt =>
                attempt.AccountId == accountId
                && attempt.Operation == operation
                && attempt.Fingerprint == fingerprint);
            if (current is null || !StringComparer.Ordinal.Equals(current.Key, key))
                throw new InvalidDataException("AI request recovery attempt is stale.");

            int generation = GetGenerationCore(accountId, operation, fingerprint);
            List<AiRequestRecoveryClaim> claims = LoadClaims();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            claims.RemoveAll(claim => claim.ExpiresAt <= now);
            AiRequestRecoveryClaim? competing = claims.FirstOrDefault(claim =>
                claim.AccountId == accountId
                && claim.Operation == operation
                && claim.Fingerprint == fingerprint);
            if (competing is not null
                && (competing.Generation != generation
                    || !StringComparer.Ordinal.Equals(competing.Key, key)))
            {
                claims.Remove(competing);
                competing = null;
            }
            if (claims.Count >= MaximumClaims)
            {
                // Expired claims were removed above. A full live set cannot be
                // safely evicted because each entry may still own a provider
                // call, so fail closed until one releases.
                throw new InvalidDataException("AI request recovery claims are full.");
            }

            string owner = $"{Guid.NewGuid():N}";
            claims.Add(new AiRequestRecoveryClaim(
                accountId,
                operation,
                fingerprint,
                key,
                generation,
                owner,
                now.Add(ClaimLifetime)));
            SaveClaims(claims);
            return new AiRequestRecoveryLease(
                this,
                accountId,
                operation,
                fingerprint,
                key,
                generation,
                owner);
        }
    }

    internal void ReleaseClaim(AiRequestRecoveryLease claim, bool force)
    {
        lock (_gate)
        {
            try
            {
                using FileStream lease = AcquireLock();
                List<AiRequestRecoveryClaim> claims = LoadClaims();
                int removed = force || !claim.IsDispatched
                    ? claims.RemoveAll(item =>
                    item.AccountId == claim.AccountId
                    && item.Operation == claim.Operation
                    && item.Fingerprint == claim.Fingerprint
                    && item.Generation == claim.Generation
                    && StringComparer.Ordinal.Equals(item.Key, claim.Key)
                    && StringComparer.Ordinal.Equals(item.OwnerToken, claim.OwnerToken))
                    : 0;
                if (removed > 0)
                    SaveClaims(claims);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
            {
            }
        }
    }

    /// <summary>
    /// Removes a settled row only when the caller still owns the exact key that
    /// created it. A stale response from an older generation therefore cannot
    /// delete a newer row with the same form fingerprint.
    /// </summary>
    internal bool TrySettle(
        string accountId,
        string operation,
        string fingerprint,
        string key,
        string? ownerToken = null,
        int? generation = null)
        => TryRemoveExact(accountId, operation, fingerprint, key, advanceGeneration: true, ownerToken, generation);

    /// <summary>Withdraws a preflight refusal using an exact key CAS.</summary>
    internal bool TryWithdraw(
        string accountId,
        string operation,
        string fingerprint,
        string key,
        string? ownerToken = null,
        int? generation = null)
        => TryRemoveExact(accountId, operation, fingerprint, key, advanceGeneration: false, ownerToken, generation);

    internal bool Abandon(AiPendingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_gate)
        {
            ValidateRecord(attempt);
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            AiPendingAttempt? existing = records.FirstOrDefault(record =>
                record.AccountId == attempt.AccountId
                && record.Operation == attempt.Operation
                && record.Fingerprint == attempt.Fingerprint);
            if (existing is null)
                return false;
            if (!StringComparer.Ordinal.Equals(existing.Key, attempt.Key))
                return false;
            List<AiRequestRecoveryClaim> claims = LoadClaims();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (claims.Any(claim => claim.AccountId == attempt.AccountId
                && claim.Operation == attempt.Operation
                && claim.Fingerprint == attempt.Fingerprint
                && claim.Key == attempt.Key
                && claim.ExpiresAt > now))
            {
                // A request actively being dispatched cannot be abandoned by a
                // competing process; the owner will release it on settle/error.
                return false;
            }

            // Advance the durable generation before publishing row removal. If
            // the row write fails, the old row remains recoverable; if the
            // generation write fails, no paid key is discarded.
            AdvanceGenerationCore(existing.AccountId, existing.Operation, existing.Fingerprint);
            records.Remove(existing);
            Save(records);
            DeleteDurableSources([existing], records);
            InvalidateClaimsCore(existing.AccountId, existing.Operation, existing.Fingerprint);
            if (existing.EffectiveSources.Count != 0)
            {
                foreach (AiRequestRecoverySource source in existing.EffectiveSources)
                {
                    if (source.DurableFile is { } durable)
                        _newSourceFiles.Remove(durable);
                }
            }
            return true;
        }
    }

    private bool TryRemoveExact(
        string accountId,
        string operation,
        string fingerprint,
        string key,
        bool advanceGeneration,
        string? ownerToken = null,
        int? generation = null)
    {
        lock (_gate)
        {
            ValidateIdentity(accountId, operation, fingerprint);
            if (!IsPrintable(key, 255))
                throw new InvalidDataException("AI request recovery key is invalid.");
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            AiPendingAttempt? existing = records.FirstOrDefault(record =>
                record.AccountId == accountId
                && record.Operation == operation
                && record.Fingerprint == fingerprint);
            if (existing is null || !StringComparer.Ordinal.Equals(existing.Key, key))
                return false;
            List<AiRequestRecoveryClaim> claims = LoadClaims();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            claims.RemoveAll(claim => claim.ExpiresAt <= now);
            if (claims.Any(claim => claim.AccountId == accountId
                && claim.Operation == operation
                && claim.Fingerprint == fingerprint
                && claim.Key == key
                && (ownerToken is null
                    || !StringComparer.Ordinal.Equals(claim.OwnerToken, ownerToken)
                    || generation is not null && claim.Generation != generation.Value)
                && claim.ExpiresAt > now))
            {
                // A live claim belongs to the dispatching process. A stale
                // response without its owner token cannot settle/withdraw it.
                return false;
            }

            if (advanceGeneration)
                AdvanceGenerationCore(accountId, operation, fingerprint);
            records.Remove(existing);
            Save(records);
            DeleteDurableSources([existing], records);
            InvalidateClaimsCore(accountId, operation, fingerprint);
            return true;
        }
    }

    private int GetGenerationCore(string accountId, string operation, string fingerprint)
        => LoadGenerations().FirstOrDefault(entry =>
            entry.AccountId == accountId
            && entry.Operation == operation
            && entry.Fingerprint == fingerprint)?.Generation ?? 0;

    internal void SettleMany(IEnumerable<AiPendingAttempt> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        AiPendingAttempt[] requested = identities.ToArray();
        foreach (AiPendingAttempt identity in requested)
        {
            ValidateIdentity(identity.AccountId, identity.Operation, identity.Fingerprint);
            if (!IsPrintable(identity.Key, 255))
                throw new InvalidDataException("AI request recovery key is invalid.");
        }

        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            List<AiPendingAttempt> records = Load();
            AiPendingAttempt[] removed = records
                .Where(record => requested.Any(identity =>
                    identity.AccountId == record.AccountId
                    && identity.Operation == record.Operation
                    && identity.Fingerprint == record.Fingerprint
                    && identity.Key == record.Key))
                .ToArray();
            if (removed.Length == 0)
                return;

            foreach (AiPendingAttempt attempt in removed)
                AdvanceGenerationCore(attempt.AccountId, attempt.Operation, attempt.Fingerprint);

            records.RemoveAll(record => requested.Any(identity =>
                identity.AccountId == record.AccountId
                && identity.Operation == record.Operation
                && identity.Fingerprint == record.Fingerprint
                && identity.Key == record.Key));
            Save(records);
            foreach (AiPendingAttempt attempt in removed)
            {
                foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
                {
                    if (source.DurableFile is { } durable)
                        _newSourceFiles.Remove(durable);
                }
            }
            DeleteDurableSources(removed, records);
        }
    }

    internal bool HasAny(string accountId, string operation)
        => PendingFor(accountId, operation).Count != 0;

    internal IReadOnlyList<string> ModelsFor(string accountId, string operation)
        => PendingFor(accountId, operation)
            .Where(record => record.Operation == operation && record.Model is not null)
            .Select(record => record.Model!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal bool HasModelless(string accountId, string operation)
        => PendingFor(accountId, operation)
            .Any(record => record.Operation == operation && record.Model is null);

    /// <summary>
    /// Makes a private, process-independent copy for a captured or generated
    /// source. The JSON row stores only the generated token and its hash.
    /// </summary>
    internal AiRequestRecoverySource CreateDurableSource(
        string role,
        string name,
        ReadOnlySpan<byte> content,
        string? elementId = null)
    {
        ValidateSourceMetadata(role, name, content.Length, elementId);
        string token = $"{Guid.NewGuid():N}.src";
        string destination = Path.Combine(_sourceDirectory, token);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WritePrivateBytes(temporary, content);
            File.Move(temporary, destination, overwrite: false);
            RestrictFile(destination);
            lock (_gate)
                _newSourceFiles.Add(token);
            return new AiRequestRecoverySource(
                role,
                Path: null,
                name,
                Convert.ToHexString(SHA256.HashData(content)),
                content.Length,
                token,
                elementId);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    /// Removes durable files that were written before a row could be
    /// committed. Only files older than the orphan grace period and not
    /// referenced by a valid row are removed; a committed copy is never
    /// guessed at or deleted.
    /// </summary>
    internal void DeleteUncommittedSources(IEnumerable<AiRequestRecoverySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        AiRequestRecoverySource[] requested = sources.ToArray();
        if (requested.Length == 0)
            return;

        lock (_gate)
        {
            try
            {
                using FileStream lease = AcquireLock();
                List<AiPendingAttempt> records;
                try
                {
                    records = Load();
                }
                catch (InvalidDataException)
                {
                    // A corrupt index is fail-closed. Orphan cleanup can be retried
                    // on a later startup once the index is repaired.
                    return;
                }

                DeleteUncommittedSourcesCore(requested, records);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
            {
                // If the cross-process lock is held, only delete copies created
                // by this store instance and not yet published in a row. A
                // committed copy is never guessed at or removed. Crashed
                // processes are handled by the startup orphan sweep.
                foreach (AiRequestRecoverySource source in requested)
                {
                    if (source.DurableFile is { } durable
                        && _newSourceFiles.Contains(durable))
                    {
                        TryDelete(Path.Combine(_sourceDirectory, durable));
                        _newSourceFiles.Remove(durable);
                    }
                }
            }
        }
    }

    private void DeleteUncommittedSourcesCore(
        IEnumerable<AiRequestRecoverySource> requested,
        IReadOnlyList<AiPendingAttempt> records)
    {
        HashSet<string> retained = records
            .SelectMany(record => record.EffectiveSources)
            .Where(source => source.DurableFile is not null)
            .Select(source => source.DurableFile!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (AiRequestRecoverySource source in requested)
        {
            if (source.DurableFile is { } durable && !retained.Contains(durable))
            {
                TryDelete(Path.Combine(_sourceDirectory, durable));
                _newSourceFiles.Remove(durable);
            }
        }
    }

    private void MarkSourcesCommitted(IEnumerable<AiRequestRecoverySource> sources)
    {
        foreach (AiRequestRecoverySource source in sources)
        {
            if (source.DurableFile is { } durable)
                _newSourceFiles.Remove(durable);
        }
    }

    private void SweepOrphanedSources()
    {
        try
        {
            using FileStream lease = AcquireLock();
            HashSet<string> retained = Load()
                .SelectMany(record => record.EffectiveSources)
                .Where(source => source.DurableFile is not null)
                .Select(source => source.DurableFile!)
                .ToHashSet(StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            foreach (string path in Directory.EnumerateFiles(_sourceDirectory, "*.src"))
            {
                string name = Path.GetFileName(path);
                if (retained.Contains(name))
                    continue;
                DateTime written = File.GetLastWriteTimeUtc(path);
                if (now - written >= OrphanSourceAge)
                    TryDelete(path);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            // Recovery is fail-closed. Never sweep when the index could not be
            // read, because a source may still be referenced by a valid row.
        }
    }

    /// <summary>
    /// Returns the original user-selected source only after checking its name,
    /// length and bytes. Any failure is false (fail closed), never a fallback
    /// to a newly named request.
    /// </summary>
    internal bool TryResolveSource(AiRequestRecoverySource source, out string? path)
    {
        path = null;
        try
        {
            ValidateSource(source);
            string candidate;
            if (source.DurableFile is { Length: > 0 } durable)
            {
                candidate = Path.Combine(_sourceDirectory, durable);
                string full = Path.GetFullPath(candidate);
                if (!IsWithinDirectory(full, _sourceDirectory))
                    return false;
            }
            else if (source.Path is { Length: > 0 } external)
            {
                candidate = external;
            }
            else
            {
                return false;
            }

            if (!File.Exists(candidate))
                return false;
            FileInfo info = new(candidate);
            if (info.Length != source.Length)
                return false;
            using FileStream stream = File.OpenRead(candidate);
            byte[] hash = SHA256.HashData(stream);
            if (!CryptographicOperations.FixedTimeEquals(
                    hash,
                    Convert.FromHexString(source.ContentHash)))
            {
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            path = null;
            return false;
        }
    }

    internal IReadOnlyList<string> ResolveSources(AiPendingAttempt attempt)
    {
        ValidateRecord(attempt);
        var result = new string[attempt.EffectiveSources.Count];
        for (int index = 0; index < result.Length; index++)
        {
            if (!TryResolveSource(attempt.EffectiveSources[index], out string? path)
                || path is null)
            {
                throw new InvalidDataException(
                    $"AI recovery source '{attempt.EffectiveSources[index].Role}' is unavailable.");
            }

            result[index] = path;
        }

        return result;
    }

    /// <summary>Reads and verifies one source in a single fail-closed operation.</summary>
    internal byte[] ReadSourceBytes(AiRequestRecoverySource source)
    {
        if (!TryResolveSource(source, out string? path) || path is null)
            throw new InvalidDataException($"AI recovery source '{source.Role}' is unavailable.");
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != source.Length
                || !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(bytes),
                    Convert.FromHexString(source.ContentHash)))
            {
                throw new InvalidDataException(
                    $"AI recovery source '{source.Role}' changed while it was read.");
            }
            return bytes;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            throw new InvalidDataException($"AI recovery source '{source.Role}' is unreadable.", ex);
        }
    }

    private List<AiPendingAttempt> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            FileInfo info = new(_path);
            if (info.Length is <= 0 or > MaximumBytes)
                throw new InvalidDataException("AI request recovery store has invalid size.");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(_path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != Version
                || !root.TryGetProperty("records", out JsonElement recordsElement)
                || recordsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Unsupported AI request recovery version.");
            }

            var result = new List<AiPendingAttempt>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in recordsElement.EnumerateArray())
            {
                AiPendingAttempt attempt = ParseAttempt(item);
                ValidateRecord(attempt);
                string identity = $"{attempt.AccountId}\n{attempt.Operation}\n{attempt.Fingerprint}";
                if (!identities.Add(identity))
                    throw new InvalidDataException("Duplicate AI recovery record.");
                result.Add(attempt);
                if (result.Count > MaximumRecords)
                    throw new InvalidDataException("AI request recovery store is full.");
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            throw new InvalidDataException("AI request recovery store is unreadable.", ex);
        }
    }

    private static AiPendingAttempt ParseAttempt(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid AI recovery record.");

        JsonProperty[] properties = item.EnumerateObject().ToArray();
        if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count()
            != properties.Length)
        {
            throw new InvalidDataException("Duplicate AI recovery record property.");
        }
        bool legacy = properties.Length == 5
            && properties.All(property => property.Name is nameof(AiPendingAttempt.AccountId)
                or nameof(AiPendingAttempt.Operation)
                or nameof(AiPendingAttempt.Fingerprint)
                or nameof(AiPendingAttempt.Key)
                or nameof(AiPendingAttempt.Model));
        bool current = properties.Length is 6 or 7
            && properties.All(property => property.Name is nameof(AiPendingAttempt.AccountId)
                or nameof(AiPendingAttempt.Operation)
                or nameof(AiPendingAttempt.Fingerprint)
                or nameof(AiPendingAttempt.Key)
                or nameof(AiPendingAttempt.Model)
                or nameof(AiPendingAttempt.Form)
                or nameof(AiPendingAttempt.Sources));
        if (!legacy && !current)
            throw new InvalidDataException("Invalid AI recovery record shape.");

        string account = RequiredString(item, nameof(AiPendingAttempt.AccountId));
        string operation = RequiredString(item, nameof(AiPendingAttempt.Operation));
        string fingerprint = RequiredString(item, nameof(AiPendingAttempt.Fingerprint));
        string key = RequiredString(item, nameof(AiPendingAttempt.Key));
        string? model = OptionalString(item, nameof(AiPendingAttempt.Model));
        AiRequestFormSnapshot? form = null;
        IReadOnlyList<AiRequestRecoverySource>? sources = null;
        if (current)
        {
            if (!item.TryGetProperty(nameof(AiPendingAttempt.Form), out JsonElement formElement)
                || formElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
            {
                throw new InvalidDataException("Invalid AI recovery form state.");
            }

            if (formElement.ValueKind == JsonValueKind.Object)
            {
                EnsureKnownProperties(
                    formElement,
                    FormProperties);
                form = JsonSerializer.Deserialize<AiRequestFormSnapshot>(formElement.GetRawText())
                    ?? throw new InvalidDataException("Invalid AI recovery form state.");
            }

            if (item.TryGetProperty(nameof(AiPendingAttempt.Sources), out JsonElement sourcesElement))
            {
                if (sourcesElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Invalid AI recovery sources.");
                foreach (JsonElement sourceElement in sourcesElement.EnumerateArray())
                    EnsureKnownProperties(sourceElement, SourceProperties);
                sources = JsonSerializer.Deserialize<List<AiRequestRecoverySource>>(
                        sourcesElement.GetRawText())
                    ?? throw new InvalidDataException("Invalid AI recovery sources.");
            }
            else
            {
                sources = Array.Empty<AiRequestRecoverySource>();
            }
        }

        return new AiPendingAttempt(account, operation, fingerprint, key, model, form, sources);
    }

    private void Save(List<AiPendingAttempt> records)
    {
        if (records.Count > MaximumRecords)
            throw new InvalidDataException(
                "AI request recovery store is full; unresolved attempts were retained.");

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new RecoveryDocument(Version, records),
            SerializerOptions);
        if (bytes.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("AI request recovery store exceeds its size limit.");

        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WritePrivateBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
            RestrictFile(_path);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private int AdvanceGenerationCore(string accountId, string operation, string fingerprint)
    {
        List<AiGenerationEntry> entries = LoadGenerations();
        int index = entries.FindIndex(entry => entry.AccountId == accountId
            && entry.Operation == operation
            && entry.Fingerprint == fingerprint);
        int next = index >= 0 ? checked(entries[index].Generation + 1) : 1;
        if (index >= 0)
            entries[index] = entries[index] with { Generation = next };
        else
        {
            if (entries.Count >= MaximumGenerationEntries)
            {
                // Generation entries are tombstones. Prune only identities with
                // no live pending row or claim; active identities keep their
                // fence and can never be reused while a stale response exists.
                HashSet<string> active = Load()
                    .Select(attempt => $"{attempt.AccountId}\n{attempt.Operation}\n{attempt.Fingerprint}")
                    .Concat(LoadClaims().Select(claim =>
                        $"{claim.AccountId}\n{claim.Operation}\n{claim.Fingerprint}"))
                    .ToHashSet(StringComparer.Ordinal);
                entries.RemoveAll(entry => !active.Contains(
                    $"{entry.AccountId}\n{entry.Operation}\n{entry.Fingerprint}"));
                if (entries.Count >= MaximumGenerationEntries)
                {
                    // This can only happen when every slot is active. Keep the
                    // oldest live fences and reject this new generation rather
                    // than evicting an active owner.
                    throw new InvalidDataException("AI request generation store is full with active attempts.");
                }
            }
            entries.Add(new AiGenerationEntry(accountId, operation, fingerprint, next));
        }

        SaveGenerations(entries);
        InvalidateClaimsCore(accountId, operation, fingerprint);
        return next;
    }

    private void InvalidateClaimsCore(string accountId, string operation, string fingerprint)
    {
        List<AiRequestRecoveryClaim> claims = LoadClaims();
        if (claims.RemoveAll(claim => claim.AccountId == accountId
            && claim.Operation == operation
            && claim.Fingerprint == fingerprint) > 0)
        {
            SaveClaims(claims);
        }
    }

    private List<AiGenerationEntry> LoadGenerations()
    {
        if (!File.Exists(_generationPath))
            return [];
        try
        {
            FileInfo info = new(_generationPath);
            if (info.Length is <= 0 or > 256 * 1024)
                throw new InvalidDataException("AI request generation store has invalid size.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(_generationPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != Version
                || !root.TryGetProperty("generations", out JsonElement generations)
                || generations.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Unsupported AI request generation version.");
            }

            var result = new List<AiGenerationEntry>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in generations.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Invalid AI request generation entry.");
                JsonProperty[] properties = item.EnumerateObject().ToArray();
                if (properties.Length != 4
                    || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 4
                    || properties.Any(property => property.Name is not (nameof(AiGenerationEntry.AccountId)
                        or nameof(AiGenerationEntry.Operation)
                        or nameof(AiGenerationEntry.Fingerprint)
                        or nameof(AiGenerationEntry.Generation))))
                {
                    throw new InvalidDataException("Invalid AI request generation shape.");
                }

                string account = RequiredString(item, nameof(AiGenerationEntry.AccountId));
                string operation = RequiredString(item, nameof(AiGenerationEntry.Operation));
                string fingerprint = RequiredString(item, nameof(AiGenerationEntry.Fingerprint));
                if (!item.TryGetProperty(nameof(AiGenerationEntry.Generation), out JsonElement number)
                    || number.ValueKind != JsonValueKind.Number
                    || number.GetInt32() <= 0)
                {
                    throw new InvalidDataException("Invalid AI request generation value.");
                }

                ValidateIdentity(account, operation, fingerprint);
                var entry = new AiGenerationEntry(account, operation, fingerprint, number.GetInt32());
                string identity = $"{account}\n{operation}\n{fingerprint}";
                if (!identities.Add(identity))
                    throw new InvalidDataException("Duplicate AI request generation entry.");
                result.Add(entry);
                if (result.Count > MaximumGenerationEntries)
                    throw new InvalidDataException("AI request generation store is full.");
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            throw new InvalidDataException("AI request generation store is unreadable.", ex);
        }
    }

    private void SaveGenerations(List<AiGenerationEntry> entries)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new GenerationDocument(Version, entries),
            SerializerOptions);
        if (bytes.Length is <= 0 or > 256 * 1024)
            throw new InvalidDataException("AI request generation store exceeds its size limit.");
        string temporary = _generationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WritePrivateBytes(temporary, bytes);
            File.Move(temporary, _generationPath, overwrite: true);
            RestrictFile(_generationPath);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private List<AiRequestRecoveryClaim> LoadClaims()
    {
        if (!File.Exists(_claimPath))
            return [];
        try
        {
            FileInfo info = new(_claimPath);
            if (info.Length is <= 0 or > 256 * 1024)
                throw new InvalidDataException("AI recovery claims have invalid size.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(_claimPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Count() != 2
                || !root.TryGetProperty("version", out JsonElement version)
                || version.GetInt32() != Version
                || !root.TryGetProperty("claims", out JsonElement claimsElement)
                || claimsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Unsupported AI recovery claim version.");
            }

            List<AiRequestRecoveryClaim>? claims = JsonSerializer.Deserialize<List<AiRequestRecoveryClaim>>(
                claimsElement.GetRawText(),
                SerializerOptions);
            if (claims is null || claims.Count > MaximumClaims)
                throw new InvalidDataException("AI recovery claim count exceeds its limit.");
            return claims;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            throw new InvalidDataException("AI recovery claims are unreadable.", ex);
        }
    }

    private void SaveClaims(List<AiRequestRecoveryClaim> claims)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new ClaimDocument(Version, claims),
            SerializerOptions);
        if (bytes.Length is <= 0 or > 256 * 1024)
            throw new InvalidDataException("AI recovery claims exceed their size limit.");
        string temporary = _claimPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WritePrivateBytes(temporary, bytes);
            File.Move(temporary, _claimPath, overwrite: true);
            RestrictFile(_claimPath);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private FileStream AcquireLock()
    {
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            FileStream stream = new(LockPath, options);
            RestrictFile(LockPath);
            return stream;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("AI request recovery store is unavailable.", ex);
        }
    }

    private void DeleteDurableSources(
        IEnumerable<AiPendingAttempt> removed,
        IEnumerable<AiPendingAttempt> remaining)
    {
        HashSet<string> retained = remaining
            .SelectMany(attempt => attempt.EffectiveSources)
            .Where(source => source.DurableFile is not null)
            .Select(source => source.DurableFile!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (AiPendingAttempt attempt in removed)
        {
            foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
            {
                if (source.DurableFile is { } durable && !retained.Contains(durable))
                    TryDelete(Path.Combine(_sourceDirectory, durable));
            }
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private sealed record RecoveryDocument(int version, IReadOnlyList<AiPendingAttempt> records);

    private sealed record GenerationDocument(int version, IReadOnlyList<AiGenerationEntry> generations);

    private sealed record ClaimDocument(int version, IReadOnlyList<AiRequestRecoveryClaim> claims);

    private sealed record AiGenerationEntry(
        string AccountId,
        string Operation,
        string Fingerprint,
        int Generation);

    private sealed record AiRequestRecoveryClaim(
        string AccountId,
        string Operation,
        string Fingerprint,
        string Key,
        int Generation,
        string OwnerToken,
        DateTimeOffset ExpiresAt);

    private static readonly HashSet<string> FormProperties =
        [
            nameof(AiRequestFormSnapshot.Prompt),
            nameof(AiRequestFormSnapshot.Style),
            nameof(AiRequestFormSnapshot.Composition),
            nameof(AiRequestFormSnapshot.Motion),
            nameof(AiRequestFormSnapshot.Exclusions),
            nameof(AiRequestFormSnapshot.Task),
            nameof(AiRequestFormSnapshot.AspectRatio),
            nameof(AiRequestFormSnapshot.Background),
            nameof(AiRequestFormSnapshot.Seed),
            nameof(AiRequestFormSnapshot.DurationSeconds),
            nameof(AiRequestFormSnapshot.Resolution),
            nameof(AiRequestFormSnapshot.GenerateAudio),
            nameof(AiRequestFormSnapshot.OutpaintExpansionPercent),
            nameof(AiRequestFormSnapshot.MaxReferenceImages),
            nameof(AiRequestFormSnapshot.MaxReferenceTotalBytes),
            nameof(AiRequestFormSnapshot.SupportsReferenceImage),
            nameof(AiRequestFormSnapshot.SupportsSeed),
            nameof(AiRequestFormSnapshot.HasBackgroundChoice),
            nameof(AiRequestFormSnapshot.SupportsAudio),
            nameof(AiRequestFormSnapshot.SupportsFirstFrame),
            nameof(AiRequestFormSnapshot.SupportsLastFrame),
            nameof(AiRequestFormSnapshot.SourceName),
            nameof(AiRequestFormSnapshot.SourceIsPrepared),
            nameof(AiRequestFormSnapshot.SourceElementId),
            nameof(AiRequestFormSnapshot.FirstFrameElementId),
            nameof(AiRequestFormSnapshot.LastFrameElementId),
        ];

    private static readonly HashSet<string> SourceProperties =
        [
            nameof(AiRequestRecoverySource.Role),
            nameof(AiRequestRecoverySource.Path),
            nameof(AiRequestRecoverySource.Name),
            nameof(AiRequestRecoverySource.ContentHash),
            nameof(AiRequestRecoverySource.Length),
            nameof(AiRequestRecoverySource.DurableFile),
            nameof(AiRequestRecoverySource.ElementId),
        ];

    private static void EnsureKnownProperties(JsonElement element, HashSet<string> known)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid AI recovery object.");
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name) || !names.Add(property.Name))
                throw new InvalidDataException("Unknown or duplicate AI recovery property.");
        }
    }

    private static string RequiredString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out JsonElement element)
            || element.ValueKind != JsonValueKind.String
            || element.GetString() is not { } value)
        {
            throw new InvalidDataException($"AI recovery property '{property}' is invalid.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out JsonElement element))
            throw new InvalidDataException($"AI recovery property '{property}' is missing.");
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new InvalidDataException($"AI recovery property '{property}' is invalid."),
        };
    }

    private static void ValidateIdentity(string accountId, string operation, string fingerprint)
    {
        ValidateText(accountId, 128, nameof(accountId));
        ValidateText(operation, 128, nameof(operation));
        ValidateText(fingerprint, 128, nameof(fingerprint));
    }

    private static void ValidateRecord(AiPendingAttempt attempt)
    {
        ValidateIdentity(attempt.AccountId, attempt.Operation, attempt.Fingerprint);
        if (!IsPrintable(attempt.Key, 255))
            throw new InvalidDataException("AI request recovery key is invalid.");
        if (attempt.Model is not null && !IsPrintable(attempt.Model, 256))
            throw new InvalidDataException("AI request recovery model is invalid.");

        if (attempt.Form is { } form)
        {
            ValidateOptionalMultilineText(form.Prompt, MaximumPromptLength, nameof(form.Prompt));
            ValidateOptionalMultilineText(form.Style, MaximumPromptLength, nameof(form.Style));
            ValidateOptionalMultilineText(form.Composition, MaximumPromptLength, nameof(form.Composition));
            ValidateOptionalMultilineText(form.Motion, MaximumPromptLength, nameof(form.Motion));
            ValidateOptionalMultilineText(form.Exclusions, MaximumPromptLength, nameof(form.Exclusions));
            ValidateOptionalText(form.Task, MaximumScalarLength, nameof(form.Task));
            ValidateOptionalText(form.AspectRatio, MaximumScalarLength, nameof(form.AspectRatio));
            ValidateOptionalText(form.Background, MaximumScalarLength, nameof(form.Background));
            ValidateOptionalText(form.Resolution, MaximumScalarLength, nameof(form.Resolution));
            ValidateOptionalText(form.SourceName, MaximumScalarLength, nameof(form.SourceName));
            if (form.MaxReferenceImages is < 0 or > AiRequestLimits.MaxImageReferences
                || form.MaxReferenceTotalBytes is <= 0 or > MaximumSourceBytes)
                throw new InvalidDataException("AI recovery capability snapshot is invalid.");
            ValidateOptionalText(form.SourceElementId, MaximumScalarLength, nameof(form.SourceElementId));
            ValidateOptionalText(form.FirstFrameElementId, MaximumScalarLength, nameof(form.FirstFrameElementId));
            ValidateOptionalText(form.LastFrameElementId, MaximumScalarLength, nameof(form.LastFrameElementId));
            if (form.Seed is < AiRequestLimits.MinSeed or > AiRequestLimits.MaxSeed
                || form.DurationSeconds is < 1 or > 300
                || form.OutpaintExpansionPercent is < 1 or > 100)
            {
                throw new InvalidDataException("AI recovery scalar parameter is invalid.");
            }
        }

        IReadOnlyList<AiRequestRecoverySource> sources = attempt.EffectiveSources;
        if (sources.Count > MaximumSourcesPerRecord)
            throw new InvalidDataException("AI recovery source count exceeds its limit.");
        foreach (AiRequestRecoverySource source in sources)
            ValidateSource(source);
    }

    private static void ValidateSource(AiRequestRecoverySource source)
    {
        ValidateSourceMetadata(source.Role, source.Name, source.Length, source.ElementId);
        if (source.Path is { } path)
        {
            if (path.Length > MaximumPathLength || path.IndexOf('\0') >= 0)
                throw new InvalidDataException("AI recovery source path is invalid.");
        }

        if (source.DurableFile is { } durable
            && (!IsPrintable(durable, 96)
                || durable.Contains('/')
                || durable.Contains('\\')
                || !durable.EndsWith(".src", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("AI recovery durable source locator is invalid.");
        }

        if (source.Path is null && source.DurableFile is null)
            throw new InvalidDataException("AI recovery source has no locator.");
        if (source.ContentHash.Length != 64
            || !source.ContentHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("AI recovery source hash is invalid.");
        }
    }

    private static void ValidateSourceMetadata(
        string role,
        string name,
        long length,
        string? elementId)
    {
        ValidateText(role, 128, nameof(role));
        ValidateText(name, 256, nameof(name));
        if (length is < 0 or > MaximumSourceBytes)
            throw new InvalidDataException("AI recovery source length is invalid.");
        if (elementId is not null)
            ValidateText(elementId, MaximumScalarLength, nameof(elementId));
    }

    private static void ValidateOptionalText(string? value, int maxLength, string name)
    {
        if (value is { Length: > 0 })
            ValidateText(value, maxLength, name);
    }

    private static void ValidateOptionalMultilineText(string? value, int maxLength, string name)
    {
        if (value is null)
            return;
        if (value.Length > maxLength
            || value.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidDataException($"AI request recovery {name} is invalid.");
        }
    }

    private static void ValidateText(string value, int maxLength, string name)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maxLength
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"AI request recovery {name} is invalid.");
        }
    }

    private static bool IsPrintable(string value, int maxLength)
        => value.Length > 0
            && value.Length <= maxLength
            && value.All(character => character is >= '\x20' and <= '\x7e');

    private static void WritePrivateBytes(string path, ReadOnlySpan<byte> bytes)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using FileStream stream = new(path, options);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        RestrictFile(path);
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        string fullPath = Path.GetFullPath(path);
        string fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.Ordinal);
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class AiRequestRecoveryContext : IDisposable
{
    private readonly FileAiRequestRecoveryStore _store;
    private readonly Func<AiAuthenticatedRequestIdentity?> _identityProvider;
    private readonly IDisposable? _identitySubscription;
    private string? _lastAccount;

    public event Action? IdentityChanged;

    public AiRequestRecoveryContext(
        FileAiRequestRecoveryStore store,
        Func<AiAuthenticatedRequestIdentity?> identityProvider,
        IObservable<AiAuthenticatedRequestIdentity?>? identityChanges = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _identitySubscription = identityChanges?.Subscribe(_ => RefreshIdentity());
    }

    public FileAiRequestRecoveryStore Store => _store;

    public AiAuthenticatedRequestIdentity GetRequiredIdentity()
    {
        AiAuthenticatedRequestIdentity identity = _identityProvider()
            ?? throw new AuthenticationRequiredException();
        if (string.IsNullOrWhiteSpace(identity.AccountId)
            || identity.User is { } user
                && !StringComparer.Ordinal.Equals(user.Profile.Id, identity.AccountId))
        {
            throw new AuthenticationRequiredException();
        }

        return identity;
    }

    public AiAuthenticatedRequestIdentity? TryGetIdentity()
    {
        AiAuthenticatedRequestIdentity? current = _identityProvider();
        if (current is { } identity
            && (string.IsNullOrWhiteSpace(identity.AccountId)
                || identity.User is { } user
                    && !StringComparer.Ordinal.Equals(user.Profile.Id, identity.AccountId)))
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

    public IReadOnlyList<AiPendingAttempt> PendingFor(string operation)
        => TryGetIdentity() is { } identity
            ? _store.PendingFor(identity.AccountId, operation)
            : Array.Empty<AiPendingAttempt>();

    public bool Abandon(AiPendingAttempt attempt)
    {
        AiAuthenticatedRequestIdentity identity = GetRequiredIdentity();
        if (!StringComparer.Ordinal.Equals(identity.AccountId, attempt.AccountId))
            throw new AuthenticationRequiredException();
        return _store.Abandon(attempt);
    }

    public void Dispose() => _identitySubscription?.Dispose();

    public IDisposable Enter(string issuedAccountId)
    {
        AiAuthenticatedRequestIdentity current = GetRequiredIdentity();
        if (!StringComparer.Ordinal.Equals(current.AccountId, issuedAccountId))
            throw new AuthenticationRequiredException();
        return current.User is null
            ? EmptyDisposable.Instance
            : AiAuthenticatedRequestScope.Enter(current.User);
    }
}
