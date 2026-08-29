using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

internal sealed class InMemoryAiRetryKeyStore : IAiRetryKeyStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _generations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AiRetryAttempt> _attempts = new(StringComparer.Ordinal);

    public bool TryGet(AiJob job, string accountId, out string key)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(Identity(job, accountId), out Entry? entry))
            {
                key = entry.Key;
                return true;
            }

            key = string.Empty;
            return false;
        }
    }

    public string GetOrCreate(AiJob job, string accountId, out bool isRepeat)
    {
        lock (_gate)
        {
            string identity = Identity(job, accountId);
            if (_entries.TryGetValue(identity, out Entry? entry))
            {
                isRepeat = true;
                return entry.Key;
            }

            isRepeat = false;
            string key = $"test-{Guid.NewGuid():N}";
            _entries[identity] = new Entry(key, Advance(identity), null);
            return key;
        }
    }

    public void Retire(AiJob job, string accountId)
    {
        lock (_gate)
        {
            string identity = Identity(job, accountId);
            if (_entries.Remove(identity))
                Advance(identity);
            RemoveAttempts(identity);
        }
    }

    public void AbandonAttempt(AiRetryAttempt attempt)
    {
        lock (_gate)
        {
            _attempts.Remove(attempt.Token);
        }
    }

    public AiRetryAttempt PrepareAttempt(AiJob job, string accountId)
    {
        lock (_gate)
        {
            string identity = Identity(job, accountId);
            if (_entries.TryGetValue(identity, out Entry? entry))
            {
                if (entry.InFlight)
                    throw new AiRetryAttemptRejectedException();
                return ExistingAttempt(job, accountId, identity, entry.Key, entry.Generation, AiRetryAttemptKind.Recovery);
            }

            long generation = _generations.GetValueOrDefault(identity);
            AiRetryAttempt? pending = _attempts.Values.FirstOrDefault(candidate =>
                candidate.AccountId == accountId
                && candidate.CanonicalIdentity == identity
                && candidate.Generation == generation
                && candidate.Kind == AiRetryAttemptKind.NewPurchase);
            if (pending is not null)
                return pending;

            string key = $"test-{Guid.NewGuid():N}";
            return ExistingAttempt(job, accountId, identity, key, generation, AiRetryAttemptKind.NewPurchase);
        }
    }

    public bool TryPrepareRecoveryAttempt(AiJob job, string accountId, out AiRetryAttempt attempt)
    {
        lock (_gate)
        {
            string identity = Identity(job, accountId);
            if (!_entries.TryGetValue(identity, out Entry? entry))
            {
                attempt = null!;
                return false;
            }

            if (entry.InFlight)
                throw new AiRetryAttemptRejectedException();
            attempt = ExistingAttempt(
                job,
                accountId,
                identity,
                entry.Key,
                entry.Generation,
                AiRetryAttemptKind.Recovery);
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
        lock (_gate)
        {
            key = string.Empty;
            isRepeat = false;
            string identity = Identity(job, accountId);
            if (!StringComparer.Ordinal.Equals(attempt.AccountId, accountId)
                || !StringComparer.Ordinal.Equals(attempt.CanonicalIdentity, identity)
                || !_attempts.TryGetValue(attempt.Token, out AiRetryAttempt? stored)
                || !Same(stored, attempt))
            {
                return false;
            }

            long generation = _generations.GetValueOrDefault(identity);
            if (generation != attempt.Generation)
            {
                _attempts.Remove(attempt.Token);
                return false;
            }

            if (attempt.Kind == AiRetryAttemptKind.Recovery)
            {
                if (!_entries.TryGetValue(identity, out Entry? entry)
                    || entry.InFlight
                    || entry.Generation != attempt.Generation
                    || entry.Key != attempt.Key)
                {
                    _attempts.Remove(attempt.Token);
                    return false;
                }

                _entries[identity] = entry with { Owner = attempt.Token };
                key = entry.Key;
                isRepeat = true;
            }
            else
            {
                if (_entries.ContainsKey(identity))
                {
                    _attempts.Remove(attempt.Token);
                    return false;
                }

                long next = Advance(identity);
                _entries.Add(identity, new Entry(attempt.Key, next, attempt.Token));
                key = attempt.Key;
            }

            _attempts.Remove(attempt.Token);
            return true;
        }
    }

    public bool TryRelease(
        AiJob job,
        string accountId,
        string key,
        long generation,
        string ownerToken)
    {
        lock (_gate)
        {
            string identity = Identity(job, accountId);
            if (!_entries.TryGetValue(identity, out Entry? entry)
                || entry.Owner is null
                || entry.Generation != generation
                || entry.Key != key
                || entry.Owner != ownerToken)
            {
                return false;
            }

            _entries[identity] = entry with { Owner = null };
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
        lock (_gate)
        {
            string identity = Identity(job, accountId);
            if (!_entries.TryGetValue(identity, out Entry? entry)
                || entry.Owner is null
                || entry.Generation != generation
                || entry.Key != key
                || entry.Owner != ownerToken)
            {
                return false;
            }

            _entries.Remove(identity);
            Advance(identity);
            RemoveAttempts(identity);
            return true;
        }
    }

    private AiRetryAttempt ExistingAttempt(
        AiJob job,
        string accountId,
        string identity,
        string key,
        long generation,
        AiRetryAttemptKind kind)
    {
        foreach (AiRetryAttempt attempt in _attempts.Values)
        {
            if (attempt.AccountId == accountId
                && attempt.CanonicalIdentity == identity
                && attempt.Key == key
                && attempt.Generation == generation
                && attempt.Kind == kind)
            {
                return attempt;
            }
        }

        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var created = new AiRetryAttempt(
            Guid.NewGuid().ToString("N"),
            accountId,
            identity,
            key,
            generation,
            kind,
            FileAiRetryKeyStore.PayloadDigest(job),
            1,
            createdAt,
            createdAt.AddMinutes(5),
            AbandonAttempt);
        _attempts.Add(created.Token, created);
        return created;
    }

    private long Advance(string identity)
    {
        long next = _generations.GetValueOrDefault(identity) + 1;
        _generations[identity] = next;
        return next;
    }

    private void RemoveAttempts(string identity)
    {
        foreach (string token in _attempts.Values
                     .Where(attempt => attempt.CanonicalIdentity == identity)
                     .Select(attempt => attempt.Token)
                     .ToArray())
        {
            _attempts.Remove(token);
        }
    }

    private static bool Same(AiRetryAttempt left, AiRetryAttempt right)
        => left.Token == right.Token
            && left.AccountId == right.AccountId
            && left.CanonicalIdentity == right.CanonicalIdentity
            && left.Key == right.Key
            && left.Generation == right.Generation
            && left.Kind == right.Kind;

    private static string Identity(AiJob job, string accountId)
        => FileAiRetryKeyStore.CanonicalIdentity(job, accountId);

    private sealed record Entry(string Key, long Generation, string? Owner)
    {
        public bool InFlight => Owner is not null;
    }
}
