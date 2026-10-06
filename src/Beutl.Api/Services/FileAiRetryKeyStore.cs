using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.Api.Services;

/// <summary>Durably keeps idempotency keys and pending confirmations for AI history retries.</summary>
internal sealed partial class FileAiRetryKeyStore : IAiRetryKeyStore
{
    private const int Version = 2;
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumEntries = 256;
    private const int MaximumGenerations = 512;
    private const int MaximumAttempts = 256;
    private const int LockAttempts = 100;
    private static readonly TimeSpan LockDelay = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _directory;
    private string LockPath => _path + ".lock";

    public FileAiRetryKeyStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _directory = Path.GetFullPath(storageDirectory);
        Directory.CreateDirectory(_directory);
        RestrictDirectory(_directory);
        _path = Path.Combine(_directory, "retry-keys.json");
        SweepTemporaryFiles();
    }

    public string GetOrCreate(AiJob job, string accountId, out bool isRepeat)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            RemoveExpiredAttempts(data);
            RemoveStaleAttempts(data);
            if (data.Entries.TryGetValue(identity, out Entry? existing))
            {
                if (existing.PayloadVersion == 0)
                {
                    existing = existing with { PayloadDigest = PayloadDigest(job), PayloadVersion = 1 };
                    data.Entries[identity] = existing;
                    Save(data);
                }
                else if (!StringComparer.Ordinal.Equals(existing.PayloadDigest, PayloadDigest(job)))
                    throw new AiRetryAttemptRejectedException();
                if (existing.IsLeaseActive)
                    throw new AiRetryAttemptRejectedException();
                if (existing.InFlightOwner is not null)
                {
                    existing = existing with { InFlightOwner = null, InFlightUntil = null };
                    data.Entries[identity] = existing;
                    Save(data);
                }

                isRepeat = true;
                return existing.Key;
            }

            isRepeat = false;
            string key = CreateKey(job);
            long generation = AdvanceGeneration(data, identity);
            data.Entries.Add(
                identity,
                new Entry(key, generation, PayloadDigest(job), 1, null, null));
            RemoveAttemptsForIdentity(data, identity);
            PruneGenerations(data);
            Save(data);
            return key;
        }
    }

    public bool TryGet(AiJob job, string accountId, out string key)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            RemoveExpiredAttempts(data);
            RemoveStaleAttempts(data);
            if (data.Entries.TryGetValue(identity, out Entry? entry)
                && entry.PayloadVersion == 1
                && StringComparer.Ordinal.Equals(entry.PayloadDigest, PayloadDigest(job)))
            {
                key = entry.Key;
                return true;
            }

            key = string.Empty;
            return false;
        }
    }

    public void Retire(AiJob job, string accountId)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            if (data.Entries.Remove(identity))
                AdvanceGeneration(data, identity);
            RemoveAttemptsForIdentity(data, identity);
            PruneGenerations(data);
            Save(data);
        }
    }

    public void AbandonAttempt(AiRetryAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            RemoveExpiredAttempts(data);
            RemoveStaleAttempts(data);
            if (data.Attempts.Remove(attempt.Token))
            {
                PruneGenerations(data);
                Save(data);
            }
        }
    }

    public bool TryRelease(
        AiJob job,
        string accountId,
        string key,
        long generation,
        string ownerToken)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            if (!data.Entries.TryGetValue(identity, out Entry? entry)
                || entry.Generation != generation
                || !StringComparer.Ordinal.Equals(entry.Key, key)
                || entry.PayloadVersion != 1
                || !StringComparer.Ordinal.Equals(entry.PayloadDigest, PayloadDigest(job))
                || !StringComparer.Ordinal.Equals(entry.InFlightOwner, ownerToken))
            {
                return false;
            }

            data.Entries[identity] = entry with { InFlightOwner = null, InFlightUntil = null };
            Save(data);
            return true;
        }
    }

    public bool TryRetire(
        AiJob job,
        string accountId,
        string key,
        long generation,
        string ownerToken)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            if (!data.Entries.TryGetValue(identity, out Entry? entry)
                || entry.Generation != generation
                || !StringComparer.Ordinal.Equals(entry.Key, key)
                || entry.PayloadVersion != 1
                || !StringComparer.Ordinal.Equals(entry.PayloadDigest, PayloadDigest(job))
                || !StringComparer.Ordinal.Equals(entry.InFlightOwner, ownerToken))
            {
                return false;
            }

            data.Entries.Remove(identity);
            AdvanceGeneration(data, identity);
            RemoveAttemptsForIdentity(data, identity);
            PruneGenerations(data);
            Save(data);
            return true;
        }
    }

    public AiRetryAttempt PrepareAttempt(AiJob job, string accountId)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            RemoveExpiredAttempts(data);
            RemoveStaleAttempts(data);

            AiRetryAttemptKind kind;
            string key;
            long generation;
            if (data.Entries.TryGetValue(identity, out Entry? entry))
            {
                if (entry.PayloadVersion == 0)
                {
                    entry = entry with { PayloadDigest = PayloadDigest(job), PayloadVersion = 1 };
                    data.Entries[identity] = entry;
                    Save(data);
                }
                else if (!StringComparer.Ordinal.Equals(entry.PayloadDigest, PayloadDigest(job)))
                    throw new AiRetryAttemptRejectedException();
                if (entry.IsLeaseActive)
                    throw new AiRetryAttemptRejectedException();
                if (entry.InFlightOwner is not null)
                {
                    entry = entry with { InFlightOwner = null, InFlightUntil = null };
                    data.Entries[identity] = entry;
                    Save(data);
                }

                kind = AiRetryAttemptKind.Recovery;
                key = entry.Key;
                generation = entry.Generation;
            }
            else
            {
                kind = AiRetryAttemptKind.NewPurchase;
                generation = GetGeneration(data, identity);
                PendingAttempt? existing = data.Attempts.Values.FirstOrDefault(candidate =>
                    candidate.AccountId == accountId
                    && candidate.Identity == identity
                    && candidate.Generation == generation
                    && candidate.Kind == kind);
                if (existing is not null)
                    return ToAttempt(existing);
                key = CreateKey(job);
            }

            PendingAttempt? matching = data.Attempts.Values.FirstOrDefault(candidate =>
                candidate.AccountId == accountId
                && candidate.Identity == identity
                && candidate.Key == key
                && candidate.Generation == generation
                && candidate.Kind == kind);
            if (matching is not null)
                return ToAttempt(matching);

            if (data.Attempts.Count >= MaximumAttempts)
                throw new AiRetryStoreUnavailableException("Retry confirmation store is full.");

            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            PendingAttempt pending = new(
                CreateOpaqueToken(),
                accountId,
                identity,
                key,
                generation,
                kind,
                PayloadDigest(job),
                1,
                createdAt,
                createdAt + LeaseDuration);
            data.Attempts.Add(pending.Token, pending);
            PruneGenerations(data);
            Save(data);
            return ToAttempt(pending);
        }
    }

    public bool TryPrepareRecoveryAttempt(
        AiJob job,
        string accountId,
        out AiRetryAttempt attempt)
    {
        string identity = CanonicalIdentity(job, accountId);
        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            RemoveExpiredAttempts(data);
            RemoveStaleAttempts(data);
            if (!data.Entries.TryGetValue(identity, out Entry? entry))
            {
                attempt = null!;
                return false;
            }

            if (entry.PayloadVersion != 1
                || !StringComparer.Ordinal.Equals(entry.PayloadDigest, PayloadDigest(job)))
                throw new AiRetryAttemptRejectedException();
            if (entry.IsLeaseActive)
                throw new AiRetryAttemptRejectedException();
            if (entry.InFlightOwner is not null)
            {
                entry = entry with { InFlightOwner = null, InFlightUntil = null };
                data.Entries[identity] = entry;
                Save(data);
            }

            PendingAttempt? existing = data.Attempts.Values.FirstOrDefault(candidate =>
                candidate.AccountId == accountId
                && candidate.Identity == identity
                && candidate.Key == entry.Key
                && candidate.Generation == entry.Generation
                && candidate.Kind == AiRetryAttemptKind.Recovery);
            if (existing is not null)
            {
                attempt = ToAttempt(existing);
                return true;
            }

            if (data.Attempts.Count >= MaximumAttempts)
                throw new AiRetryStoreUnavailableException("Retry confirmation store is full.");
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            PendingAttempt pending = new(
                CreateOpaqueToken(),
                accountId,
                identity,
                entry.Key,
                entry.Generation,
                AiRetryAttemptKind.Recovery,
                entry.PayloadDigest,
                entry.PayloadVersion,
                createdAt,
                createdAt + LeaseDuration);
            data.Attempts.Add(pending.Token, pending);
            PruneGenerations(data);
            Save(data);
            attempt = ToAttempt(pending);
            return true;
        }
    }

    public bool TryConsumeAttempt(
        AiRetryAttempt attempt,
        AiJob job,
        string accountId,
        out string key,
        out bool isRepeat)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        string identity = CanonicalIdentity(job, accountId);
        key = string.Empty;
        isRepeat = false;
        if (!StringComparer.Ordinal.Equals(attempt.AccountId, accountId)
            || !StringComparer.Ordinal.Equals(attempt.CanonicalIdentity, identity)
            || !StringComparer.Ordinal.Equals(attempt.PayloadDigest, PayloadDigest(job))
            || attempt.PayloadVersion != 1)
        {
            return false;
        }

        lock (_gate)
        {
            using FileStream lease = AcquireLock();
            StoreData data = Load();
            RemoveExpiredAttempts(data);
            if (!data.Attempts.TryGetValue(attempt.Token, out PendingAttempt? pending)
                || !pending.Matches(attempt))
            {
                return false;
            }

            long generation = GetGeneration(data, identity);
            if (generation != attempt.Generation)
            {
                data.Attempts.Remove(attempt.Token);
                PruneGenerations(data);
                Save(data);
                return false;
            }

            DateTimeOffset leaseUntil = DateTimeOffset.UtcNow + LeaseDuration;
            if (attempt.Kind == AiRetryAttemptKind.Recovery)
            {
                if (!data.Entries.TryGetValue(identity, out Entry? entry)
                    || entry.Generation != attempt.Generation
                    || !StringComparer.Ordinal.Equals(entry.Key, attempt.Key)
                    || entry.PayloadVersion != attempt.PayloadVersion
                    || !StringComparer.Ordinal.Equals(entry.PayloadDigest, attempt.PayloadDigest)
                    || entry.IsLeaseActive)
                {
                    data.Attempts.Remove(attempt.Token);
                    PruneGenerations(data);
                    Save(data);
                    return false;
                }

                key = entry.Key;
                isRepeat = true;
                data.Entries[identity] = entry with
                {
                    InFlightOwner = attempt.Token,
                    InFlightUntil = leaseUntil,
                };
            }
            else
            {
                if (data.Entries.ContainsKey(identity))
                {
                    data.Attempts.Remove(attempt.Token);
                    PruneGenerations(data);
                    Save(data);
                    return false;
                }

                long nextGeneration = AdvanceGeneration(data, identity);
                data.Entries.Add(
                    identity,
                    new Entry(
                        attempt.Key,
                        nextGeneration,
                        attempt.PayloadDigest,
                        attempt.PayloadVersion,
                        attempt.Token,
                        leaseUntil));
                key = attempt.Key;
            }

            // The pending confirmation is consumed atomically with the claim.
            // The durable key remains until the provider outcome is definitive.
            data.Attempts.Remove(attempt.Token);
            PruneGenerations(data);
            Save(data);
            return true;
        }
    }

    internal static string CanonicalIdentity(AiJob job, string accountId)
    {
        ArgumentNullException.ThrowIfNull(job);
        ValidateAccount(accountId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v1\n{accountId}\n{job.Id.Value}")));
    }

    internal static string PayloadDigest(AiJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        string input = CanonicalizePayload(job);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v1\n{job.Kind.Value}\n{job.Model?.Value}\n{input}")));
    }

    private static string CanonicalizePayload(AiJob job)
    {
        if (job.InputParameters is not { } input)
            return string.Empty;
        using JsonDocument document = JsonDocument.Parse(input.GetRawText());
        return CanonicalizeJson(document.RootElement);
    }

    private static string CanonicalizeJson(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => JsonSerializer.Serialize(property.Name)
                    + ":" + CanonicalizeJson(property.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(CanonicalizeJson)) + "]",
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.Number => CanonicalizeNumber(element),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => string.Empty,
        };

    private static string CanonicalizeNumber(JsonElement element)
    {
        if (element.TryGetDecimal(out decimal decimalValue))
            return decimalValue.ToString("G29", CultureInfo.InvariantCulture);
        if (element.TryGetDouble(out double doubleValue)
            && double.IsFinite(doubleValue))
            return doubleValue.ToString("R", CultureInfo.InvariantCulture);
        throw new AiRetryAttemptRejectedException();
    }

    private static string CreateKey(AiJob job)
        => $"history-retry:{job.Id.Value}:{Guid.NewGuid():N}";

    private static string CreateOpaqueToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static long GetGeneration(StoreData data, string identity)
        => data.Generations.TryGetValue(identity, out long generation) ? generation : 0;

    private static long AdvanceGeneration(StoreData data, string identity)
    {
        long next = checked(GetGeneration(data, identity) + 1);
        data.Generations[identity] = next;
        return next;
    }

    private static void RemoveAttemptsForIdentity(StoreData data, string identity)
    {
        foreach (string token in data.Attempts.Values
                     .Where(attempt => attempt.Identity == identity)
                     .Select(attempt => attempt.Token)
                     .ToArray())
        {
            data.Attempts.Remove(token);
        }
    }

    private static void RemoveStaleAttempts(StoreData data)
    {
        foreach (PendingAttempt attempt in data.Attempts.Values.ToArray())
        {
            long generation = GetGeneration(data, attempt.Identity);
            bool stale = generation != attempt.Generation;
            if (!stale && attempt.Kind == AiRetryAttemptKind.Recovery)
            {
                stale = !data.Entries.TryGetValue(attempt.Identity, out Entry? entry)
                    || entry.Generation != attempt.Generation
                    || entry.Key != attempt.Key
                    || entry.PayloadVersion != attempt.PayloadVersion
                    || entry.PayloadDigest != attempt.PayloadDigest
                    || entry.IsLeaseActive;
            }
            else if (!stale)
            {
                stale = data.Entries.ContainsKey(attempt.Identity);
            }

            if (stale)
                data.Attempts.Remove(attempt.Token);
        }
    }

    private static void RemoveExpiredAttempts(StoreData data)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (PendingAttempt attempt in data.Attempts.Values
                     .Where(candidate => candidate.ExpiresAt <= now)
                     .ToArray())
        {
            data.Attempts.Remove(attempt.Token);
        }
    }

    private static void PruneGenerations(StoreData data)
    {
        if (data.Generations.Count <= MaximumGenerations)
            return;

        HashSet<string> retained = new(data.Entries.Keys, StringComparer.Ordinal);
        retained.UnionWith(data.Attempts.Values.Select(attempt => attempt.Identity));
        foreach (string identity in data.Generations.Keys
                     .Where(identity => !retained.Contains(identity))
                     .ToArray())
        {
            data.Generations.Remove(identity);
            if (data.Generations.Count <= MaximumGenerations)
                break;
        }
    }

    private AiRetryAttempt ToAttempt(PendingAttempt pending)
        => new(
            pending.Token,
            pending.AccountId,
            pending.Identity,
            pending.Key,
            pending.Generation,
            pending.Kind,
            pending.PayloadDigest,
            pending.PayloadVersion,
            pending.CreatedAt,
            pending.ExpiresAt,
            AbandonAttempt);

    private static void ValidateAccount(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 256)
            throw new AuthenticationRequiredException();
    }

    private sealed class StoreData
    {
        public Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> Generations { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PendingAttempt> Attempts { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Entry(
        string Key,
        long Generation,
        string PayloadDigest,
        int PayloadVersion,
        string? InFlightOwner,
        DateTimeOffset? InFlightUntil)
    {
        public bool IsLeaseActive
            => InFlightOwner is { Length: > 0 }
                && InFlightUntil is { } until
                && until > DateTimeOffset.UtcNow;
    }

    private sealed record PendingAttempt(
        string Token,
        string AccountId,
        string Identity,
        string Key,
        long Generation,
        AiRetryAttemptKind Kind,
        string PayloadDigest,
        int PayloadVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt)
    {
        public bool Matches(AiRetryAttempt attempt)
            => Token == attempt.Token
                && AccountId == attempt.AccountId
                && Identity == attempt.CanonicalIdentity
                && Key == attempt.Key
                && Generation == attempt.Generation
                && Kind == attempt.Kind
                && PayloadDigest == attempt.PayloadDigest
                && PayloadVersion == attempt.PayloadVersion
                && CreatedAt == attempt.CreatedAt
                && ExpiresAt == attempt.ExpiresAt;
    }
}
