using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beutl.Api.Objects;

namespace Beutl.Api.Services;

internal enum AiRetryAttemptKind
{
    Recovery,
    NewPurchase,
}

/// <summary>Opaque, single-use confirmation data produced by a retry preflight.</summary>
internal sealed class AiRetryAttempt : IDisposable
{
    private readonly Action<AiRetryAttempt> _abandon;
    private int _disposed;

    public AiRetryAttempt(
        string token,
        string accountId,
        string canonicalIdentity,
        string key,
        long generation,
        AiRetryAttemptKind kind,
        string payloadDigest,
        int payloadVersion,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        Action<AiRetryAttempt> abandon)
    {
        Token = token;
        AccountId = accountId;
        CanonicalIdentity = canonicalIdentity;
        Key = key;
        Generation = generation;
        Kind = kind;
        PayloadDigest = payloadDigest;
        PayloadVersion = payloadVersion;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        _abandon = abandon ?? throw new ArgumentNullException(nameof(abandon));
    }

    public string Token { get; }

    public string AccountId { get; }

    public string CanonicalIdentity { get; }

    public string Key { get; }

    public long Generation { get; }

    public AiRetryAttemptKind Kind { get; }

    public string PayloadDigest { get; }

    public int PayloadVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                _abandon(this);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Expiry pruning is the fail-safe when the store is locked or
                // corrupt during UI teardown; cancellation must remain best effort.
            }
        }
    }

    public override bool Equals(object? obj)
        => obj is AiRetryAttempt other
            && StringComparer.Ordinal.Equals(Token, other.Token);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Token);
}

internal interface IAiRetryKeyStore
{
    bool TryGet(AiJob job, string accountId, out string key);

    string GetOrCreate(AiJob job, string accountId, out bool isRepeat);

    void Retire(AiJob job, string accountId);

    AiRetryAttempt PrepareAttempt(AiJob job, string accountId);

    bool TryPrepareRecoveryAttempt(
        AiJob job,
        string accountId,
        out AiRetryAttempt attempt);

    void AbandonAttempt(AiRetryAttempt attempt);

    bool TryConsumeAttempt(
        AiRetryAttempt attempt,
        AiJob job,
        string accountId,
        out string key,
        out bool isRepeat);

    bool TryRelease(
        AiJob job,
        string accountId,
        string key,
        long generation,
        string ownerToken);

    bool TryRetire(
        AiJob job,
        string accountId,
        string key,
        long generation,
        string ownerToken);
}

internal sealed class AiRetryStoreUnavailableException(string message, Exception? inner = null)
    : IOException(message, inner);

internal sealed class AiRetryAttemptRejectedException()
    : InvalidOperationException("The retry confirmation is no longer valid. Start a new confirmation.");

/// <summary>Durably keeps idempotency keys and pending confirmations for AI history retries.</summary>
internal sealed class FileAiRetryKeyStore : IAiRetryKeyStore
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

    private StoreData Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new StoreData();
            long length = new FileInfo(_path).Length;
            if (length is <= 0 or > MaximumBytes)
                throw new AiRetryStoreUnavailableException("Retry key store is unreadable.");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(_path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number)
            {
                throw new AiRetryStoreUnavailableException("Retry key store has an unsupported version.");
            }

            int value = version.GetInt32();
            return value switch
            {
                1 => LoadVersion1(root),
                Version => LoadVersion2(root),
                _ => throw new AiRetryStoreUnavailableException("Retry key store has an unsupported version."),
            };
        }
        catch (AiRetryStoreUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or OverflowException
            or InvalidOperationException
            or ArgumentException)
        {
            throw new AiRetryStoreUnavailableException("Retry key store could not be read.", ex);
        }
    }

    private static StoreData LoadVersion1(JsonElement root)
    {
        if (root.EnumerateObject().Count() != 2
            || !root.TryGetProperty("entries", out JsonElement entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array)
        {
            throw new AiRetryStoreUnavailableException("Retry key store has an invalid legacy shape.");
        }

        var data = new StoreData();
        int count = 0;
        foreach (JsonElement element in entriesElement.EnumerateArray())
        {
            if (++count > MaximumEntries
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 2
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("key", out JsonElement key))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid entries.");
            }

            string identityValue = ReadIdentity(identity);
            string keyValue = ReadKey(key);
            if (!data.Entries.TryAdd(identityValue, new Entry(keyValue, 1, string.Empty, 0, null, null)))
                throw new AiRetryStoreUnavailableException("Retry key store contains duplicate entries.");
            data.Generations[identityValue] = 1;
        }

        return data;
    }

    private static StoreData LoadVersion2(JsonElement root)
    {
        if (root.EnumerateObject().Count() != 4
            || !root.TryGetProperty("entries", out JsonElement entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("generations", out JsonElement generationsElement)
            || generationsElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("attempts", out JsonElement attemptsElement)
            || attemptsElement.ValueKind != JsonValueKind.Array)
        {
            throw new AiRetryStoreUnavailableException("Retry key store has an invalid shape.");
        }

        var data = new StoreData();
        int generationCount = 0;
        foreach (JsonElement element in generationsElement.EnumerateArray())
        {
            if (++generationCount > MaximumGenerations
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 2
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("generation", out JsonElement generation)
                || generation.ValueKind != JsonValueKind.Number)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid generations.");
            }

            string identityValue = ReadIdentity(identity);
            long generationValue = generation.GetInt64();
            if (generationValue < 0 || !data.Generations.TryAdd(identityValue, generationValue))
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid generations.");
        }

        int entryCount = 0;
        foreach (JsonElement element in entriesElement.EnumerateArray())
        {
            if (++entryCount > MaximumEntries
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 7
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("key", out JsonElement key)
                || !element.TryGetProperty("generation", out JsonElement generation)
                || !element.TryGetProperty("payloadDigest", out JsonElement payloadDigest)
                || !element.TryGetProperty("payloadVersion", out JsonElement payloadVersion)
                || !element.TryGetProperty("inFlightOwner", out JsonElement owner)
                || !element.TryGetProperty("inFlightUntil", out JsonElement until)
                || generation.ValueKind != JsonValueKind.Number
                || payloadDigest.ValueKind != JsonValueKind.String
                || payloadVersion.ValueKind != JsonValueKind.Number
                || owner.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || until.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid entries.");
            }

            string identityValue = ReadIdentity(identity);
            string keyValue = ReadKey(key);
            long generationValue = generation.GetInt64();
            string payloadDigestValue = ReadDigest(payloadDigest);
            int payloadVersionValue = payloadVersion.GetInt32();
            string? ownerValue = owner.ValueKind == JsonValueKind.Null ? null : owner.GetString();
            DateTimeOffset? untilValue = until.ValueKind == JsonValueKind.Null
                ? null
                : until.GetDateTimeOffset();
            if (ownerValue is not null && !IsPrintable(ownerValue))
                throw new AiRetryStoreUnavailableException("Retry key store contains an invalid lease owner.");
            if (ownerValue is null != (untilValue is null)
                || payloadVersionValue <= 0
                || generationValue <= 0
                || !data.Entries.TryAdd(
                    identityValue,
                    new Entry(
                        keyValue,
                        generationValue,
                        payloadDigestValue,
                        payloadVersionValue,
                        ownerValue,
                        untilValue))
                || (!data.Generations.TryGetValue(identityValue, out long recorded)
                    ? !data.Generations.TryAdd(identityValue, generationValue)
                    : recorded != generationValue))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid or duplicate entries.");
            }
        }

        int attemptCount = 0;
        foreach (JsonElement element in attemptsElement.EnumerateArray())
        {
            if (++attemptCount > MaximumAttempts
                || element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Count() != 10
                || !element.TryGetProperty("token", out JsonElement token)
                || !element.TryGetProperty("accountId", out JsonElement account)
                || !element.TryGetProperty("identity", out JsonElement identity)
                || !element.TryGetProperty("key", out JsonElement key)
                || !element.TryGetProperty("generation", out JsonElement generation)
                || !element.TryGetProperty("kind", out JsonElement kind)
                || !element.TryGetProperty("payloadDigest", out JsonElement payloadDigest)
                || !element.TryGetProperty("payloadVersion", out JsonElement payloadVersion)
                || !element.TryGetProperty("createdAt", out JsonElement createdAt)
                || !element.TryGetProperty("expiresAt", out JsonElement expiresAt)
                || generation.ValueKind != JsonValueKind.Number
                || token.ValueKind != JsonValueKind.String
                || account.ValueKind != JsonValueKind.String
                || kind.ValueKind != JsonValueKind.String
                || payloadDigest.ValueKind != JsonValueKind.String
                || payloadVersion.ValueKind != JsonValueKind.Number
                || createdAt.ValueKind != JsonValueKind.String
                || expiresAt.ValueKind != JsonValueKind.String)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid attempts.");
            }

            string tokenValue = token.GetString() ?? string.Empty;
            string accountValue = account.GetString() ?? string.Empty;
            string identityValue = ReadIdentity(identity);
            string keyValue = ReadKey(key);
            long generationValue = generation.GetInt64();
            string payloadDigestValue = ReadDigest(payloadDigest);
            int payloadVersionValue = payloadVersion.GetInt32();
            DateTimeOffset createdAtValue = createdAt.GetDateTimeOffset();
            DateTimeOffset expiresAtValue = expiresAt.GetDateTimeOffset();
            AiRetryAttemptKind kindValue = kind.GetString() switch
            {
                "recovery" => AiRetryAttemptKind.Recovery,
                "newPurchase" => AiRetryAttemptKind.NewPurchase,
                _ => throw new AiRetryStoreUnavailableException("Retry key store contains an invalid attempt kind."),
            };
            if (tokenValue.Length is < 32 or > 128
                || !IsPrintable(tokenValue)
                || accountValue.Length is 0 or > 256
                || generationValue < 0
                || payloadVersionValue <= 0
                || expiresAtValue <= createdAtValue
                || !data.Attempts.TryAdd(
                    tokenValue,
                    new PendingAttempt(
                        tokenValue,
                        accountValue,
                        identityValue,
                        keyValue,
                        generationValue,
                        kindValue,
                        payloadDigestValue,
                        payloadVersionValue,
                        createdAtValue,
                        expiresAtValue)))
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains invalid or duplicate attempts.");
            }
        }

        foreach ((string identity, Entry entry) in data.Entries)
        {
            if (!data.Generations.TryGetValue(identity, out long generation)
                || generation != entry.Generation)
            {
                throw new AiRetryStoreUnavailableException("Retry key store contains inconsistent generations.");
            }
        }

        return data;
    }

    private static string ReadIdentity(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid identity.");
        string value = element.GetString() ?? string.Empty;
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid identity.");
        return value;
    }

    private static string ReadKey(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid key.");
        string value = element.GetString() ?? string.Empty;
        if (!IsPrintable(value))
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid key.");
        return value;
    }

    private static string ReadDigest(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid payload digest.");
        string value = element.GetString() ?? string.Empty;
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new AiRetryStoreUnavailableException("Retry key store contains an invalid payload digest.");
        return value;
    }

    private void Save(StoreData data)
    {
        if (data.Entries.Count > MaximumEntries
            || data.Generations.Count > MaximumGenerations
            || data.Attempts.Count > MaximumAttempts)
        {
            throw new AiRetryStoreUnavailableException("Retry key store is full.");
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = Version,
            entries = data.Entries
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    identity = pair.Key,
                    key = pair.Value.Key,
                    generation = pair.Value.Generation,
                    payloadDigest = pair.Value.PayloadDigest,
                    payloadVersion = pair.Value.PayloadVersion,
                    inFlightOwner = pair.Value.InFlightOwner,
                    inFlightUntil = pair.Value.InFlightUntil,
                }),
            generations = data.Generations
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { identity = pair.Key, generation = pair.Value }),
            attempts = data.Attempts.Values
                .OrderBy(attempt => attempt.Token, StringComparer.Ordinal)
                .Select(attempt => new
                {
                    token = attempt.Token,
                    accountId = attempt.AccountId,
                    identity = attempt.Identity,
                    key = attempt.Key,
                    generation = attempt.Generation,
                    kind = attempt.Kind == AiRetryAttemptKind.Recovery
                        ? "recovery"
                        : "newPurchase",
                    payloadDigest = attempt.PayloadDigest,
                    payloadVersion = attempt.PayloadVersion,
                    createdAt = attempt.CreatedAt,
                    expiresAt = attempt.ExpiresAt,
                }),
        });
        if (bytes.Length > MaximumBytes)
            throw new AiRetryStoreUnavailableException("Retry key store is full.");

        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WritePrivateBytes(temporary, bytes);
            AtomicReplace(temporary, _path, overwrite: true);
            RestrictFile(_path);
            EnsureDirectorySynced(_directory);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private FileStream AcquireLock()
    {
        IOException? last = null;
        for (int attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                FileStream stream = new(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                RestrictFile(LockPath);
                return stream;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(LockDelay);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new AiRetryStoreUnavailableException("Retry key store is in use.", ex);
            }
        }

        throw new AiRetryStoreUnavailableException("Retry key store is in use.", last);
    }

    private void SweepTemporaryFiles()
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
            foreach (string path in Directory.EnumerateFiles(_directory, "retry-keys.json.*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff
                    && !IsFileLocked(path))
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsFileLocked(string path)
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

    private static bool IsPrintable(string value)
        => value.Length is > 0 and <= 255 && value.All(c => c is >= '\x20' and <= '\x7e');

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
            throw new IOException("Atomic retry-store replacement failed.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    /// <summary>
    /// Unix directory fsync closes the rename durability window. Windows has
    /// no portable directory fsync; file bytes are flushed and rename is
    /// atomic, while directory-entry persistence remains filesystem-defined.
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

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int UnixFsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int UnixClose(int fd);

    private static void ValidateAccount(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 256)
            throw new AuthenticationRequiredException();
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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

internal readonly record struct AiAuthenticatedRequestIdentity(
    string AccountId,
    AuthenticatedUser? User);

internal sealed class AiRetryAttemptContext(
    IAiRetryKeyStore store,
    Func<AiAuthenticatedRequestIdentity?> identityProvider,
    bool allowSyntheticIdentity = false) : IBeutlApiResource
{
    public IAiRetryKeyStore Store { get; } = store
        ?? throw new ArgumentNullException(nameof(store));

    private bool AllowSyntheticIdentity { get; } = allowSyntheticIdentity;

    public AiAuthenticatedRequestIdentity GetRequiredIdentity()
    {
        AiAuthenticatedRequestIdentity identity = identityProvider()
            ?? throw new AuthenticationRequiredException();
        if (string.IsNullOrWhiteSpace(identity.AccountId)
            || identity.User is null && !AllowSyntheticIdentity
            || identity.User is { } user
                && !StringComparer.Ordinal.Equals(user.Profile.Id, identity.AccountId))
        {
            throw new AuthenticationRequiredException();
        }
        return identity;
    }

    public IDisposable Enter(AiAuthenticatedRequestIdentity identity)
        => identity.User is null
            ? EmptyDisposable.Instance
            : AiAuthenticatedRequestScope.Enter(identity.User);
}

internal static class AiAuthenticatedRequestScope
{
    private static readonly AsyncLocal<AuthenticatedUser?> s_current = new();

    public static AuthenticatedUser? Current => s_current.Value;

    public static IDisposable Enter(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        AuthenticatedUser? previous = s_current.Value;
        s_current.Value = user;
        return new Scope(previous);
    }

    private sealed class Scope(AuthenticatedUser? previous) : IDisposable
    {
        private AuthenticatedUser? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                s_current.Value = _previous;
                _previous = null;
            }
        }
    }
}

internal sealed class EmptyDisposable : IDisposable
{
    public static EmptyDisposable Instance { get; } = new();

    public void Dispose()
    {
    }
}
