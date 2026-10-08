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

    AiRetryAttempt PrepareAttempt(AiJob job, string accountId);

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
