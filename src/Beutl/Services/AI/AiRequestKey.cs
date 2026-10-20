using System.Globalization;
using System.Reactive.Disposables;
using System.Security.Cryptography;
using System.Text;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.Services.AI;

/// <summary>
/// A request's name, and whether it has been sent under that name before.
/// </summary>
/// <remarks>
/// The server finds the job a repeat already reserved before it checks the balance, so a caller must not check
/// the balance for a repeat, or it refuses to collect something already bought.
/// </remarks>
internal readonly record struct AiRequestName(string Key, bool IsRepeat);

/// <summary>
/// Which refusals happened before the server reserved anything.
/// </summary>
internal static class AiRequestOutcome
{
    /// <summary>
    /// Whether this failure proves the name is not the route back to a job. Authentication and outer upload-size
    /// refusals come before the key lookup, so they withdraw only a first attempt known to be unreserved; the
    /// remaining refusals come only after the server finds no job under the key.
    /// </summary>
    public static bool CanWithdraw(AiRequestName name, Exception exception)
    {
        bool preLookupRefusal = exception is AiFileTooLargeException
            || exception is AuthenticationRequiredException
            {
                CurrentAttemptReservationIsKnownAbsent: true,
            };
        if (!name.IsRepeat && preLookupRefusal)
        {
            return true;
        }

        return exception is AiPlanRequiredException
            or AiUsageLimitExceededException
            or AiJobLimitReachedException
            or AiModelUnavailableException
            or AiModelDoesNotSupportRequestException;
    }
}

/// <summary>
/// Names the requests of one metered AI attempt, so a request whose answer never arrived goes back out under
/// the key it first used and recovers what it may already have paid for instead of buying it again.
/// </summary>
/// <remarks>
/// Every part that identifies a request goes to <see cref="NameFor(ReadOnlySpan{string})"/>, as the server
/// refuses a key with a different fingerprint. A settled key is spent: <see cref="Retire(AiRequestName)"/> and
/// <see cref="Retire()"/> start fresh names, and <see cref="WithdrawAfterNoReservation(AiRequestName)"/> takes
/// back a name the server made no job under.
/// </remarks>
internal sealed class AiRequestKey : IDisposable
{
    private readonly Lock _gate = new();
    // The names issued and the requests they represent. Only unsettled requests remain here.
    private readonly Dictionary<string, Issued> _outstanding = new(StringComparer.Ordinal);
    // A settled request cannot be sent again under the same name, so its next name takes the next generation.
    private readonly Dictionary<string, int> _generations = new(StringComparer.Ordinal);
    private readonly ReactivePropertySlim<bool> _hasOutstandingName = new(false);
    private readonly AiRequestRecoveryContext? _recoveryContext;
    private readonly string? _operation;
    private string _seed;
    private bool _resumedAttemptPending;
    private bool _disposed;

    public AiRequestKey(string? seed = null, bool namePending = false,
        AiRequestRecoveryContext? recoveryContext = null, string? operation = null)
    {
        _recoveryContext = recoveryContext;
        _operation = operation;
        _seed = string.IsNullOrEmpty(seed) ? NewSeed() : seed;
        // A restored execution knows its name but not which request was sent, so treat only the
        // first name handed out again as a repeat.
        _resumedAttemptPending = !string.IsNullOrEmpty(seed) && namePending;
        _hasOutstandingName.Value = _resumedAttemptPending;
        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged += RefreshIdentity;
    }

    /// <summary>
    /// What the current keys are derived from. Held with the rest of a resumable
    /// run's state so a request resumed in another session is sent under the key
    /// it was first sent under.
    /// </summary>
    public string Seed
    {
        get
        {
            lock (_gate)
                return _seed;
        }
    }

    public static string NewSeed() => Guid.NewGuid().ToString("N");

    internal string? CurrentAccountId => _recoveryContext?.TryGetIdentity()?.AccountId;

    /// <summary>
    /// Whether a name has been handed out that the server has not been seen to settle.
    /// </summary>
    /// <remarks>
    /// While this holds, a caller that gates on the balance has to leave the way open (see <see cref="AiRequestName"/>).
    /// </remarks>
    public IReadOnlyReactiveProperty<bool> HasOutstandingName
    {
        get
        {
            RefreshIdentity();
            return _hasOutstandingName;
        }
    }

    private void RefreshIdentity()
    {
        if (_disposed) return;
        bool has;
        lock (_gate)
            has = _resumedAttemptPending || HasCurrentOutstanding();
        _hasOutstandingName.Value = has;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged -= RefreshIdentity;
        _hasOutstandingName.Dispose();
    }

    /// <summary>
    /// The key for a request identified by <paramref name="parts"/>, and
    /// whether it has been handed out since the last settlement.
    /// </summary>
    public AiRequestName NameFor(params ReadOnlySpan<string?> parts)
    {
        string operation = ResolveOperation(parts);
        return Remember(Fingerprint(parts), operation, ResolveModel(operation, parts));
    }

    public AiRequestName NameFor(int pieceIndex, params ReadOnlySpan<string?> parts)
    {
        string operation = ResolveOperation(parts);
        return Remember(
            $"{Fingerprint(parts)}-{pieceIndex.ToString(CultureInfo.InvariantCulture)}",
            operation,
            ResolveModel(operation, parts));
    }

    internal IReadOnlyList<AiPendingAttempt> PendingAttempts(AiOperationId operation)
        => _recoveryContext?.PendingFor(operation.Value)
            ?? Array.Empty<AiPendingAttempt>();

    // Lets go of a pending request: settled, abandoned or never made. A job it made stays in the account's job list.
    internal void Settle(AiPendingAttempt attempt)
    {
        if (_recoveryContext is null)
            return;
        if (!StringComparer.Ordinal.Equals(CurrentAccountId, attempt.AccountId))
            throw new AuthenticationRequiredException();
        _recoveryContext.Abandon(attempt);
        lock (_gate)
            Forget(attempt.Key, advance: true);
        RefreshIdentity();
    }

    public IDisposable EnterAuthenticatedScope(AiRequestName name)
    {
        if (_recoveryContext is null)
            return Disposable.Empty;
        string account;
        lock (_gate)
        {
            account = _outstanding.GetValueOrDefault(name.Key).Account
                ?? throw new InvalidOperationException("The AI request name is not outstanding.");
        }
        return _recoveryContext.Enter(account);
    }

    /// <summary>
    /// Identifies bytes already in hand, sent under <paramref name="fileName"/>.
    /// </summary>
    /// <remarks>
    /// Name the reading that is sent: reading again to send would let a file rewritten in between go out under a
    /// name that describes something else.
    /// </remarks>
    public static string FileStamp(string fileName, ReadOnlySpan<byte> content)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{fileName}:{Convert.ToHexString(SHA256.HashData(content))}");

    /// <summary>
    /// Identifies bytes by the bytes alone, for an upload the server takes no
    /// name from.
    /// </summary>
    /// <remarks>
    /// A frame is named by its contents, never by its file name, so capturing it again to retry an interrupted
    /// run does not buy the run a second time.
    /// </remarks>
    public static string ContentStamp(ReadOnlySpan<byte> content)
        => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>
    /// Says the server settled the job this one name made. Only that request starts again under a fresh name.
    /// </summary>
    /// <remarks>
    /// Settling the whole run instead would strand every other outstanding request and buy it again on retry.
    /// </remarks>
    public bool Retire(AiRequestName name) => Release(name, advance: true);

    /// <summary>
    /// Says every request of this run is settled, and the next one starts under
    /// a seed of its own.
    /// </summary>
    public bool Retire()
    {
        lock (_gate)
        {
            foreach (KeyValuePair<string, Issued> pair in _outstanding)
                RemovePersisted(pair.Key, pair.Value);
            _seed = NewSeed();
            _outstanding.Clear();
            _generations.Clear();
            // Nothing under the new seed has been paid for.
            _resumedAttemptPending = false;
        }

        _hasOutstandingName.Value = false;
        return true;
    }

    /// <summary>
    /// Withdraws a name the server reported no reservation for, so the same request may go out under it again.
    /// </summary>
    internal bool WithdrawAfterNoReservation(AiRequestName name) => Release(name, advance: false);

    private bool Release(AiRequestName name, bool advance)
    {
        if (string.IsNullOrEmpty(name.Key))
            return false;

        bool released;
        bool anyLeft;
        lock (_gate)
        {
            released = _outstanding.TryGetValue(name.Key, out Issued issued);
            if (released)
            {
                RemovePersisted(name.Key, issued);
                Forget(name.Key, advance);
            }

            anyLeft = HasCurrentOutstanding();
        }

        if (!anyLeft)
            _hasOutstandingName.Value = false;
        return released;
    }

    private AiRequestName Remember(string request, string operation, string? model)
    {
        string? account = _recoveryContext?.GetRequiredIdentity().AccountId;
        AiRequestName name;
        lock (_gate)
        {
            string identity = GenerationIdentity(account, operation, request);
            if (_recoveryContext is not null)
            {
                if (operation.Length == 0)
                    throw new InvalidOperationException("An AI request operation is required for durable recovery.");
                // A name this run still holds whose row is gone was settled elsewhere, so it is spent.
                foreach (string spent in _outstanding
                             .Where(pair => GenerationIdentity(pair.Value.Account, pair.Value.Operation, pair.Value.Fingerprint) == identity)
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    if (!_recoveryContext.Store.PendingFor(account!, operation)
                            .Any(row => row.Fingerprint == request && row.Key == spent))
                    {
                        Forget(spent, advance: true);
                    }
                }

                (AiPendingAttempt row, bool existed) = _recoveryContext.Store.GetOrAdd(
                    account!,
                    operation,
                    request,
                    model,
                    () => ComposeKey(request, account, operation, _generations.GetValueOrDefault(identity)));
                bool resumed = _resumedAttemptPending;
                _resumedAttemptPending = false;
                name = new AiRequestName(row.Key, existed || resumed || _outstanding.ContainsKey(row.Key));
                _outstanding[row.Key] = new Issued(request, account, operation);
            }
            else
            {
                string key = ComposeKey(request, account, operation, _generations.GetValueOrDefault(identity));
                if (_outstanding.ContainsKey(key))
                    return new AiRequestName(key, true);
                bool resumed = _resumedAttemptPending;
                _resumedAttemptPending = false;
                name = new AiRequestName(key, resumed);
                _outstanding[key] = new Issued(request, account, operation);
            }
        }

        _hasOutstandingName.Value = true;
        return name;
    }

    // Generation 0 has no suffix. The format has shipped, so it must not change.
    private string ComposeKey(string request, string? account, string operation, int generation)
    {
        string keyBase = _recoveryContext is null
            ? $"{_seed}-{request}"
            : $"{_seed}-{request}-{ScopeFingerprint(account!, operation)}";
        return generation == 0
            ? keyBase
            : $"{keyBase}-r{generation.ToString(CultureInfo.InvariantCulture)}";
    }

    // Forgets a name; advance makes the request's next name a new one. Called under _gate.
    private void Forget(string key, bool advance)
    {
        if (!_outstanding.Remove(key, out Issued issued) || !advance)
            return;
        string identity = GenerationIdentity(issued.Account, issued.Operation, issued.Fingerprint);
        _generations[identity] = _generations.GetValueOrDefault(identity) + 1;
    }

    private void RemovePersisted(string key, Issued issued)
    {
        if (_recoveryContext is null || issued.Account is null)
            return;
        try
        {
            _recoveryContext.Store.Remove(new AiPendingAttempt(issued.Account, issued.Operation, issued.Fingerprint, key));
        }
        catch (InvalidDataException)
        {
            // The row stays listed; abandoning it or looking its job up settles it later.
        }
    }

    private string GenerationIdentity(string? account, string? operation, string request)
        => _recoveryContext is null ? request : $"{account}\n{operation}\n{request}";

    private bool HasCurrentOutstanding()
    {
        string? account = _recoveryContext?.TryGetIdentity()?.AccountId;
        return _recoveryContext is null
            ? _outstanding.Count > 0
            : account is not null && _outstanding.Values.Any(issued => issued.Account == account);
    }

    private string ResolveOperation(ReadOnlySpan<string?> parts)
    {
        if (_operation is null)
            return string.Empty;
        if (_operation == "image.edit" && parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
        {
            // The edit task is the first request part; include it in the
            // durable identity so different edit tools never collide.
            string task = parts[0]!.Trim();
            return $"image.edit.{task}";
        }
        return _operation;
    }

    private static string? ResolveModel(string operation, ReadOnlySpan<string?> parts)
    {
        int index = operation switch
        {
            "image.generate" => 4,
            "video.generate" or "video.edit" or "video.extend" or "video.motion" => 6,
            _ when operation.StartsWith("image.edit.", StringComparison.Ordinal) => 2,
            _ => -1,
        };
        return index >= 0 && index < parts.Length ? parts[index] : null;
    }

    // Length-prefixed so that no arrangement of parts can read as another one.
    private static string Fingerprint(ReadOnlySpan<string?> parts)
    {
        var builder = new StringBuilder();
        foreach (string? part in parts)
        {
            builder.Append(part?.Length ?? -1)
                .Append(':')
                .Append(part)
                .Append('\u001f');
        }

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())).AsSpan(0, 8))
            .ToLowerInvariant();
    }

    private static string ScopeFingerprint(string account, string operation)
        => Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{account}\u001f{operation}"))
                .AsSpan(0, 8))
            .ToLowerInvariant();

    private readonly record struct Issued(string Fingerprint, string? Account, string Operation);
}
