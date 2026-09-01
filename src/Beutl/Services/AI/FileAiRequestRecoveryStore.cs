using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
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
    private const int ActiveState = 0;
    private const int DispatchingState = 1;
    private const int DispatchedState = 2;
    private const int ReleasedState = 3;
    private int _state;
    private int _everDispatched;
    private Timer? _renewalTimer;
    private int _renewing;
    private static readonly TimeSpan RenewalCadence = TimeSpan.FromMinutes(5);

    internal Action? BeforeReacquirePublish { get; set; }

    internal AiRequestRecoveryLease(
        FileAiRequestRecoveryStore store,
        string accountId,
        string operation,
        string fingerprint,
        string key,
        int generation,
        string ownerToken,
        bool dispatched = false)
    {
        _store = store;
        AccountId = accountId;
        Operation = operation;
        Fingerprint = fingerprint;
        Key = key;
        Generation = generation;
        OwnerToken = ownerToken;
        _state = dispatched ? DispatchedState : ActiveState;
        _everDispatched = dispatched ? 1 : 0;
        if (dispatched)
            StartRenewalTimer();
    }

    internal string AccountId { get; }

    internal string Operation { get; }

    internal string Fingerprint { get; }

    internal string Key { get; }

    internal int Generation { get; }

    internal string OwnerToken { get; }

    internal bool IsDispatched => Volatile.Read(ref _state) == DispatchedState;

    internal bool IsReleased => Volatile.Read(ref _state) == ReleasedState;

    internal bool WasDispatched => Volatile.Read(ref _everDispatched) != 0;

    internal bool MarkDispatched()
    {
        while (true)
        {
            int state = Volatile.Read(ref _state);
            if (state == DispatchedState)
                return true;
            if (state == DispatchingState)
            {
                Thread.Yield();
                continue;
            }
            if (state != ActiveState
                || Interlocked.CompareExchange(ref _state, DispatchingState, ActiveState) != ActiveState)
                return false;
            break;
        }

        bool persisted;
        try
        {
            persisted = _store.MarkClaimDispatched(this);
        }
        catch
        {
            Volatile.Write(ref _state, ActiveState);
            throw;
        }
        if (!persisted)
        {
            Volatile.Write(ref _state, ActiveState);
            return false;
        }

        StartRenewalTimer();
        Volatile.Write(ref _everDispatched, 1);
        if (Interlocked.CompareExchange(ref _state, DispatchedState, DispatchingState)
            != DispatchingState)
        {
            Interlocked.Exchange(ref _renewalTimer, null)?.Dispose();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Extends the durable dispatched fence. Call on a cadence shorter than
    /// <see cref="FileAiRequestRecoveryStore.ClaimLifetime"/> while provider
    /// work is active; returns false after expiry, settle, or owner loss.
    /// </summary>
    internal bool Renew()
        => Volatile.Read(ref _state) == DispatchedState
            && _store.RenewClaim(this);

    /// <summary>
    /// Reacquires this process's own dispatched fence after an unknown result.
    /// The owner token is part of the compare-and-swap, so another process can
    /// never take over through this path.
    /// </summary>
    internal bool Reacquire()
    {
        int state = Volatile.Read(ref _state);
        if (Volatile.Read(ref _everDispatched) == 0
            || state == DispatchingState)
            return false;

        if (state != DispatchedState
            && Interlocked.CompareExchange(ref _state, DispatchingState, ReleasedState)
                != ReleasedState)
            return false;
        if (state == DispatchedState
            && Interlocked.CompareExchange(ref _state, DispatchingState, DispatchedState)
                != DispatchedState)
            return false;

        try
        {
            bool reacquired = _store.ReacquireClaim(this);
            if (reacquired)
            {
                StartRenewalTimer();
                BeforeReacquirePublish?.Invoke();
                if (Interlocked.CompareExchange(ref _state, DispatchedState, DispatchingState)
                    != DispatchingState)
                {
                    Interlocked.Exchange(ref _renewalTimer, null)?.Dispose();
                    return false;
                }
            }
            else
            {
                Volatile.Write(ref _state, ReleasedState);
                Interlocked.Exchange(ref _renewalTimer, null)?.Dispose();
            }
            return reacquired;
        }
        catch
        {
            // A failed lock/read/write must not strand the lease in the
            // transitional state. Dispose must remain terminating.
            Volatile.Write(ref _state, ReleasedState);
            Interlocked.Exchange(ref _renewalTimer, null)?.Dispose();
            throw;
        }
    }

    private void StartRenewalTimer()
    {
        Timer? timer = _renewalTimer;
        if (timer is null)
        {
            timer = new Timer(
                static state => ((AiRequestRecoveryLease)state!).RenewTick(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            Timer? previous = Interlocked.CompareExchange(ref _renewalTimer, timer, null);
            if (previous is not null)
            {
                timer.Dispose();
                timer = previous;
            }
        }
        timer.Change(RenewalCadence, RenewalCadence);
    }

    private void RenewTick()
    {
        if (Volatile.Read(ref _state) != DispatchedState
            || Interlocked.Exchange(ref _renewing, 1) != 0)
            return;
        try
        {
            if (!Renew())
                _renewalTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("AI recovery claim renewal failed: {0}", ex.Message);
        }
        finally
        {
            Volatile.Write(ref _renewing, 0);
        }
    }

    public void Dispose()
    {
        while (true)
        {
            int state = Volatile.Read(ref _state);
            if (state == ReleasedState)
                return;
            if (state == DispatchingState)
            {
                Thread.Yield();
                continue;
            }
            if (Interlocked.CompareExchange(ref _state, ReleasedState, state) == state)
                break;
        }

        Timer? timer = Interlocked.Exchange(ref _renewalTimer, null);
        timer?.Dispose();
        _store.ReleaseClaim(this, force: false);
    }
}

/// <summary>Atomic, bounded local storage for unresolved metered AI attempts.</summary>
internal sealed class FileAiRequestRecoveryStore : IDisposable
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
    private readonly Dictionary<string, FileStream> _pendingPublicationMarkers = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly string _sourceDirectory;
    private readonly string _generationPath;
    private readonly string _claimPath;
    private readonly Func<DateTimeOffset> _utcNow;
    private string LockPath => _path + ".lock";

    public FileAiRequestRecoveryStore(
        string storageDirectory,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        string fullDirectory = Path.GetFullPath(storageDirectory);
        Directory.CreateDirectory(fullDirectory);
        RestrictDirectory(fullDirectory);
        _path = Path.Combine(fullDirectory, "ai-request-recovery.json");
        _sourceDirectory = Path.Combine(fullDirectory, SourceDirectoryName);
        _generationPath = Path.Combine(fullDirectory, "ai-request-recovery-generations.json");
        _claimPath = Path.Combine(fullDirectory, "ai-request-recovery-claims.json");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_sourceDirectory);
        RestrictDirectory(_sourceDirectory);
        SweepStoreTemporaryFiles(fullDirectory);
        SweepOrphanedSources();
    }

    internal string StoragePath => _path;

    internal string SourceDirectory => _sourceDirectory;

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (FileStream marker in _pendingPublicationMarkers.Values)
                marker.Dispose();
            _pendingPublicationMarkers.Clear();
        }
    }

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
            MarkSourcesCommitted(attempt.EffectiveSources);
            return attempt;
        }
    }

    internal AiRequestRecoveryLease? Claim(
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
            DateTimeOffset now = _utcNow();
            // A dispatched claim is a durable paid-job fence. Expiry controls
            // renewal only; it cannot make the request abandonable or reusable.
            claims.RemoveAll(claim => !claim.Dispatched && claim.ExpiresAt <= now);
            int removedStale = claims.RemoveAll(claim =>
                claim.AccountId == accountId
                && claim.Operation == operation
                && claim.Fingerprint == fingerprint
                && (claim.Generation != generation
                    || !StringComparer.Ordinal.Equals(claim.Key, key)));
            AiRequestRecoveryClaim? existingClaim = claims.FirstOrDefault(claim =>
                claim.AccountId == accountId
                && claim.Operation == operation
                && claim.Fingerprint == fingerprint);
            if (existingClaim is not null
                && existingClaim.Dispatched
                && existingClaim.ExpiresAt <= now)
            {
                // The renewal TTL has elapsed, but a dispatched provider call
                // remains fenced. Atomically hand the durable fence to the
                // recovering process, preserving key and generation while
                // fencing the old owner token.
                string adoptedOwner = $"{Guid.NewGuid():N}";
                claims[claims.IndexOf(existingClaim)] = existingClaim with
                {
                    OwnerToken = adoptedOwner,
                    ExpiresAt = now.Add(ClaimLifetime),
                };
                SaveClaims(claims);
                return new AiRequestRecoveryLease(
                    this,
                    accountId,
                    operation,
                    fingerprint,
                    key,
                    generation,
                    adoptedOwner,
                    dispatched: true);
            }
            if (existingClaim is not null)
            {
                if (removedStale > 0)
                    SaveClaims(claims);
                return null;
            }
            if (claims.Count >= MaximumClaims)
            {
                // Expired non-dispatched claims were removed above. Dispatched
                // fences are retained for exact-key adoption and cannot be
                // evicted while they may own a provider call.
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
                now.Add(ClaimLifetime),
                Dispatched: false));
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

    /// <summary>Persists the dispatch fence and extends its lease.</summary>
    internal bool MarkClaimDispatched(AiRequestRecoveryLease claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            List<AiRequestRecoveryClaim> claims = LoadClaims();
            int index = claims.FindIndex(item => MatchesClaim(item, claim));
            if (index < 0)
                return false;
            DateTimeOffset now = _utcNow();
            AiRequestRecoveryClaim current = claims[index];
            if (current.ExpiresAt <= now)
                return false;
            claims[index] = current with
            {
                Dispatched = true,
                ExpiresAt = now.Add(ClaimLifetime),
            };
            SaveClaims(claims);
            return true;
        }
    }

    /// <summary>Renews a live dispatched claim using owner/generation CAS.</summary>
    internal bool RenewClaim(AiRequestRecoveryLease claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            List<AiRequestRecoveryClaim> claims = LoadClaims();
            int index = claims.FindIndex(item => MatchesClaim(item, claim));
            if (index < 0)
                return false;
            DateTimeOffset now = _utcNow();
            AiRequestRecoveryClaim current = claims[index];
            if (!current.Dispatched || current.ExpiresAt <= now)
                return false;
            claims[index] = current with { ExpiresAt = now.Add(ClaimLifetime) };
            SaveClaims(claims);
            return true;
        }
    }

    /// <summary>
    /// Renews a dispatched fence using its original owner token, including
    /// after its TTL elapsed. A competing process may claim an expired fence
    /// first; in that case the exact-owner match fails closed.
    /// </summary>
    internal bool ReacquireClaim(AiRequestRecoveryLease claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            List<AiRequestRecoveryClaim> claims = LoadClaims();
            int index = claims.FindIndex(item => MatchesClaim(item, claim));
            if (index < 0)
                return false;
            AiRequestRecoveryClaim current = claims[index];
            if (!current.Dispatched)
                return false;
            claims[index] = current with { ExpiresAt = _utcNow().Add(ClaimLifetime) };
            SaveClaims(claims);
            return true;
        }
    }

    private static bool MatchesClaim(AiRequestRecoveryClaim item, AiRequestRecoveryLease claim)
        => item.AccountId == claim.AccountId
            && item.Operation == claim.Operation
            && item.Fingerprint == claim.Fingerprint
            && item.Generation == claim.Generation
            && item.Key == claim.Key
            && item.OwnerToken == claim.OwnerToken;

    internal void ReleaseClaim(AiRequestRecoveryLease claim, bool force)
    {
        lock (_gate)
        {
            try
            {
                using FileStream lease = AcquireLock();
                List<AiRequestRecoveryClaim> claims = LoadClaims();
                int removed = force || !claim.WasDispatched
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

    /// <summary>
    /// Withdraws a dispatched request only with the exact owner lease that
    /// marked provider dispatch, after an authoritative no-reservation result.
    /// </summary>
    internal bool TryWithdrawAfterNoReservation(
        string accountId,
        string operation,
        string fingerprint,
        string key,
        string ownerToken,
        int generation)
        => TryRemoveExact(
            accountId,
            operation,
            fingerprint,
            key,
            advanceGeneration: false,
            ownerToken,
            generation,
            requireDispatchedOwner: true);

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
            DateTimeOffset now = _utcNow();
            if (claims.Any(claim => claim.AccountId == attempt.AccountId
                && claim.Operation == attempt.Operation
                && claim.Fingerprint == attempt.Fingerprint
                && claim.Key == attempt.Key
                && (claim.Dispatched || claim.ExpiresAt > now)))
            {
                // A dispatched request cannot be abandoned by a competing
                // process. The exact owner (or a later fence adopter) must
                // settle it; expiry alone is not proof of provider terminality.
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
        int? generation = null,
        bool requireDispatchedOwner = false)
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
            DateTimeOffset now = _utcNow();
            claims.RemoveAll(claim => !claim.Dispatched && claim.ExpiresAt <= now);
            AiRequestRecoveryClaim? matchingDispatched = claims.FirstOrDefault(claim =>
                claim.AccountId == accountId
                && claim.Operation == operation
                && claim.Fingerprint == fingerprint
                && claim.Key == key
                && claim.Dispatched);
            if (requireDispatchedOwner && matchingDispatched is null)
                return false;
            if (matchingDispatched is not null
                && (ownerToken is null
                    || !StringComparer.Ordinal.Equals(matchingDispatched.OwnerToken, ownerToken)
                    || generation is null
                    || matchingDispatched.Generation != generation.Value))
            {
                // Once provider work was dispatched, only the exact owner can
                // settle it or withdraw it after an authoritative no-reservation
                // response. Process loss and lease expiry are not terminality.
                return false;
            }
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

    internal bool SettleMany(
        string accountId,
        string operation,
        IEnumerable<AiPendingAttempt> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ValidateText(accountId, 128, nameof(accountId));
        ValidateText(operation, 128, nameof(operation));
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
            AiPendingAttempt[] scoped = records.Where(record =>
                record.AccountId == accountId
                && (record.Operation == operation
                    || record.Operation.StartsWith(operation + ".", StringComparison.Ordinal)))
                .ToArray();
            // Whole-run retirement is one CAS over the complete operation
            // scope. A row that was replaced, or one this process never
            // materialized, keeps every row and generation unchanged.
            if (scoped.Length != requested.Length
                || scoped.Any(record => !requested.Any(identity =>
                    identity.AccountId == record.AccountId
                    && identity.Operation == record.Operation
                    && identity.Fingerprint == record.Fingerprint
                    && identity.Key == record.Key)))
            {
                return false;
            }
            AiPendingAttempt[] removed = scoped;
            if (removed.Length == 0)
                return false;

            List<AiRequestRecoveryClaim> claims = LoadClaims();
            if (removed.Any(attempt => claims.Any(claim =>
                claim.AccountId == attempt.AccountId
                && claim.Operation == attempt.Operation
                && claim.Fingerprint == attempt.Fingerprint
                && claim.Key == attempt.Key
                && claim.Dispatched)))
            {
                // Bulk retirement has no owner-token proof. Never remove a
                // row whose provider request was dispatched; the exact owner
                // or a later fence adopter must settle it individually.
                throw new InvalidDataException(
                    "A dispatched AI recovery attempt cannot be settled in bulk.");
            }

            foreach (AiPendingAttempt attempt in removed)
                AdvanceGenerationCore(attempt.AccountId, attempt.Operation, attempt.Fingerprint);

            records.RemoveAll(record => requested.Any(identity =>
                identity.AccountId == record.AccountId
                && identity.Operation == record.Operation
                && identity.Fingerprint == record.Fingerprint
                && identity.Key == record.Key));
            Save(records);
            foreach (AiPendingAttempt attempt in removed)
                InvalidateClaimsCore(attempt.AccountId, attempt.Operation, attempt.Fingerprint);
            foreach (AiPendingAttempt attempt in removed)
            {
                foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
                {
                    if (source.DurableFile is { } durable)
                        _newSourceFiles.Remove(durable);
                }
            }
            DeleteDurableSources(removed, records);
            return true;
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
        string marker = destination + ".pending";
        bool published = false;
        FileStream? markerLease = null;
        try
        {
            // The marker is durable before the source is published. Startup
            // sweeping therefore cannot mistake a long row-publication stall
            // for an orphan until the marker lease expires.
            var markerOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough | FileOptions.SequentialScan,
            };
            if (!OperatingSystem.IsWindows())
                markerOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            markerLease = new FileStream(marker, markerOptions);
            markerLease.Write(System.Text.Encoding.UTF8.GetBytes(_utcNow().ToString("O")));
            markerLease.Flush(flushToDisk: true);
            RestrictFile(marker);
            EnsureDirectorySynced(_sourceDirectory);
            WritePrivateBytes(temporary, content);
            AtomicReplace(temporary, destination, overwrite: false);
            published = true;
            RestrictFile(destination);
            EnsureDirectorySynced(_sourceDirectory);
            lock (_gate)
            {
                _newSourceFiles.Add(token);
                _pendingPublicationMarkers[token] = markerLease
                    ?? throw new IOException("AI recovery source publication marker was lost.");
                markerLease = null;
            }
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
            markerLease?.Dispose();
            TryDelete(temporary);
            if (!published)
                TryDelete(marker);
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
                        TryDelete(Path.Combine(_sourceDirectory, durable + ".pending"));
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
                if (_pendingPublicationMarkers.Remove(durable, out FileStream? marker))
                    marker.Dispose();
                TryDelete(Path.Combine(_sourceDirectory, durable + ".pending"));
                _newSourceFiles.Remove(durable);
            }
        }
    }

    private void MarkSourcesCommitted(IEnumerable<AiRequestRecoverySource> sources)
    {
        foreach (AiRequestRecoverySource source in sources)
        {
            if (source.DurableFile is { } durable)
            {
                _newSourceFiles.Remove(durable);
                if (_pendingPublicationMarkers.Remove(durable, out FileStream? marker))
                    marker.Dispose();
                TryDelete(Path.Combine(_sourceDirectory, durable + ".pending"));
            }
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
            DateTime now = _utcNow().UtcDateTime;
            foreach (string path in Directory.EnumerateFiles(_sourceDirectory))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".pending", StringComparison.Ordinal))
                {
                    // A crash can happen after the publication marker is
                    // flushed but before the source rename. There is then no
                    // `.src` entry below to reclaim the marker. Keep a marker
                    // while its owner is active or inside the grace period;
                    // once it is old and unlocked, it is an orphan temp file.
                    string sourcePath = path[..^".pending".Length];
                    bool sourceExists = File.Exists(sourcePath);
                    bool markerLocked = IsMarkerLocked(path);
                    bool markerIsOld = now - File.GetLastWriteTimeUtc(path) >= OrphanSourceAge;
                    if (sourceExists)
                    {
                        // Once the source is referenced by a durable row, a
                        // stale marker is no longer needed. This closes the
                        // crash window between row publication and marker
                        // deletion without touching an unregistered source.
                        if (retained.Contains(Path.GetFileName(sourcePath))
                            && !markerLocked
                            && markerIsOld)
                        {
                            TryDelete(path);
                        }
                        continue;
                    }

                    if (markerLocked || !markerIsOld)
                    {
                        continue;
                    }

                    TryDelete(path);
                    continue;
                }
                if (name.Contains(".src.", StringComparison.Ordinal)
                    && name.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    if (now - File.GetLastWriteTimeUtc(path) >= OrphanSourceAge
                        && !IsMarkerLocked(path))
                        TryDelete(path);
                    continue;
                }
                if (!name.EndsWith(".src", StringComparison.Ordinal))
                    continue;
                if (retained.Contains(name))
                    continue;
                DateTime written = File.GetLastWriteTimeUtc(path);
                string marker = path + ".pending";
                if (File.Exists(marker)
                    && (now - File.GetLastWriteTimeUtc(marker) < OrphanSourceAge
                        || IsMarkerLocked(marker)))
                    continue;
                if (now - written >= OrphanSourceAge)
                {
                    TryDelete(path);
                    TryDelete(marker);
                }
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

    private static void SweepStoreTemporaryFiles(string directory)
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow - OrphanSourceAge;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json.*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff
                    && !IsMarkerLocked(path))
                    TryDelete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
            AtomicReplace(temporary, _path, overwrite: true);
            RestrictFile(_path);
            EnsureDirectorySynced(Path.GetDirectoryName(_path)!);
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
            AtomicReplace(temporary, _generationPath, overwrite: true);
            RestrictFile(_generationPath);
            EnsureDirectorySynced(Path.GetDirectoryName(_generationPath)!);
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
            HashSet<string> identities = new(StringComparer.Ordinal);
            foreach (AiRequestRecoveryClaim claim in claims)
            {
                ValidateIdentity(claim.AccountId, claim.Operation, claim.Fingerprint);
                if (!IsPrintable(claim.Key, 255)
                    || claim.Generation < 0
                    || !IsPrintable(claim.OwnerToken, 128)
                    || !identities.Add($"{claim.AccountId}\n{claim.Operation}\n{claim.Fingerprint}\n{claim.OwnerToken}"))
                {
                    throw new InvalidDataException("AI recovery claim is invalid or duplicated.");
                }
            }
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
            AtomicReplace(temporary, _claimPath, overwrite: true);
            RestrictFile(_claimPath);
            EnsureDirectorySynced(Path.GetDirectoryName(_claimPath)!);
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
                {
                    TryDelete(Path.Combine(_sourceDirectory, durable));
                    if (_pendingPublicationMarkers.Remove(durable, out FileStream? marker))
                        marker.Dispose();
                    TryDelete(Path.Combine(_sourceDirectory, durable + ".pending"));
                }
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
        DateTimeOffset ExpiresAt,
        bool Dispatched = false);

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
        if (string.IsNullOrEmpty(source.ContentHash)
            || source.ContentHash.Length != 64
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

    private static void AtomicReplace(string temporary, string destination, bool overwrite)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Move(temporary, destination, overwrite);
            return;
        }
        const uint replace = 0x1;
        const uint writeThrough = 0x8;
        if (!MoveFileEx(temporary, destination, writeThrough | (overwrite ? replace : 0)))
            throw new IOException("Atomic recovery-file replacement failed.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    /// <summary>
    /// Flushes directory metadata after rename on Unix. Windows provides no
    /// portable directory fsync; file contents are flushed and rename is
    /// atomic, but directory-entry durability is delegated to the filesystem.
    /// </summary>
    private static void EnsureDirectorySynced(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        int fd = UnixOpen(path, 0);
        if (fd < 0)
            throw new IOException($"Unable to open directory for durability sync (errno {Marshal.GetLastWin32Error()}).");
        try
        {
            if (UnixFsync(fd) != 0)
                throw new IOException($"Unable to fsync directory (errno {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            if (UnixClose(fd) != 0)
                throw new IOException($"Unable to close synced directory (errno {Marshal.GetLastWin32Error()}).");
        }
    }

    private static bool IsMarkerLocked(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int UnixFsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int UnixClose(int fd);

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

    public void Dispose()
    {
        _identitySubscription?.Dispose();
        _store.Dispose();
    }

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
