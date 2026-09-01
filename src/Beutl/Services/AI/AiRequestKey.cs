using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.Services.AI;

/// <summary>
/// A request's name, and whether it has been sent under that name before.
/// </summary>
/// <remarks>
/// A repeat may be answered by the job the first attempt already reserved, and
/// the server looks that job up before it looks at what the account can afford.
/// A caller that checks the balance itself must not check it for a repeat, or
/// it refuses to collect something already bought — most visibly when the
/// request that emptied the balance is the one being collected.
/// </remarks>
internal readonly record struct AiRequestName(string Key, bool IsRepeat);

/// <summary>
/// Which refusals happened before the server reserved anything.
/// </summary>
internal static class AiRequestOutcome
{
    /// <summary>
    /// Whether <paramref name="exception"/> is a refusal the server decided
    /// after looking the request's name up and finding nothing under it — the
    /// sign-in, the plan, the balance, the size of an upload, how many jobs are
    /// already running, whether the model is still offered, and whether it can
    /// serve the request at all. No job was made, so the name is not the way
    /// back to one.
    /// </summary>
    public static bool ReservedNothing(Exception exception)
        => exception is AiPlanRequiredException
            or AiUsageLimitExceededException
            or AiJobLimitReachedException
            or AiModelUnavailableException
            or AiModelDoesNotSupportRequestException
            or AiFileTooLargeException;
}

/// <summary>
/// Names the requests of one metered AI attempt, so that repeating a request
/// whose answer never arrived recovers what it may already have paid for
/// instead of buying it again.
/// </summary>
/// <remarks>
/// <para>
/// The server charges an operation when it accepts it, and answers a repeat of
/// a key it has already seen with the job that key created — the finished
/// result, or a refusal while it is still running. Neither charges twice, so a
/// request whose outcome the client never learned has to go back out under the
/// key it first used. A request made of anything else is a different request
/// and takes a different key: the server refuses a key that comes back with a
/// different fingerprint behind it, which is why every part that identifies the
/// request is passed to <see cref="NameFor(ReadOnlySpan{string})"/>.
/// </para>
/// <para>
/// Once the server has settled a job — a result, or a failure it owns and has
/// refunded — the key that named it is spent. It would keep answering with that
/// settled job, so asking for anything more has to be a new request:
/// <see cref="Retire(AiRequestName)"/> says one request was settled, and
/// <see cref="Retire()"/> says the whole run was. A name the server made no job
/// under is not settled but spent for nothing, and
/// <see cref="Withdraw(AiRequestName)"/> takes it back.
/// </para>
/// </remarks>
internal sealed class AiRequestKey : IDisposable
{
    private readonly Lock _gate = new();
    // 出した名前と、それが何の依頼のものか。決着していないものだけが入っている。
    private readonly Dictionary<string, string> _outstanding = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _outstandingAccounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _outstandingOperations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AiRequestRecoveryLease> _claims = new(StringComparer.Ordinal);
    // 決着した依頼は、その名前ではもう何も頼めない。同じ依頼をもう一度出すときは
    // 次の世代の名前で出す——ここに何も無ければ第 0 世代で、名前の形は変わらない。
    private readonly Dictionary<string, int> _generations = new(StringComparer.Ordinal);
    private readonly ReactivePropertySlim<bool> _hasOutstandingName = new(false);
    private readonly AiRequestRecoveryContext? _recoveryContext;
    private readonly string? _operation;
    private string _seed;
    private bool _resumedAttemptPending;
    private bool _disposed;

    internal Action? BeforeAbandonPersistedRemoval { get; set; }

    public AiRequestKey(string? seed = null, bool namePending = false,
        AiRequestRecoveryContext? recoveryContext = null, string? operation = null)
    {
        _recoveryContext = recoveryContext;
        _operation = operation;
        _seed = string.IsNullOrEmpty(seed) ? NewSeed() : seed;
        // 控えから拾い直した実行のうち、送った直後に終わったものだけが名前を
        // 抱えている。抱えていたかどうかは控えに書いてあり、seed があることでは
        // 分からない——予約されなかった依頼や、返金済みの依頼の seed も残る。
        bool resumedSeed = !string.IsNullOrEmpty(seed) && namePending;
        bool hasDurableRecovery = false;
        if (_recoveryContext?.TryGetIdentity() is { } identity
            && !string.IsNullOrWhiteSpace(operation))
        {
            try
            {
                hasDurableRecovery = _recoveryContext.Store.HasAny(identity.AccountId, operation);
            }
            catch (InvalidDataException)
            {
                // A corrupt or locked store must not be treated as an empty
                // store. Remember the failure so the first new request fails
                // closed instead of buying work without a durable recovery row.
            }
        }
        // A paid request may have exhausted the current balance. Publish only
        // command reachability here; NameFor marks repeat only after the exact
        // fingerprint is found, so an unrelated input cannot skip preflight.
        _hasOutstandingName.Value = resumedSeed || hasDurableRecovery;
        // 拾い直した実行は名前を持っていても「どれを送ったか」は持っていないので、
        // 最初に配り直す名前を一度だけ再送として扱う。
        _resumedAttemptPending = resumedSeed;
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

    internal bool HasDurableRecovery => _recoveryContext is not null;

    internal string? CurrentAccountId => _recoveryContext?.TryGetIdentity()?.AccountId;

    /// <summary>
    /// Whether a name has been handed out that the server has not been seen to
    /// settle.
    /// </summary>
    /// <remarks>
    /// While this holds, asking again may be answered by a job already paid
    /// for. The server looks that job up before it looks at the balance, so a
    /// caller that gates on the balance itself has to leave the way open — most
    /// of all when the request being collected is the one that emptied it.
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
        bool has = false;
        lock (_gate)
        {
            // The key is still useful without durable recovery (unit callers and
            // the pre-recovery subtitle flow use that mode). In that case the
            // in-memory map is the complete source of truth.
            if (_recoveryContext is null)
            {
                has = _outstanding.Count > 0 || _resumedAttemptPending;
            }
            else
            {
                string? account = _recoveryContext?.TryGetIdentity()?.AccountId;
                has = _resumedAttemptPending
                    || account is not null && _outstandingAccounts.Values.Any(value => value == account);
                if (!has && account is not null && !string.IsNullOrWhiteSpace(_operation))
                {
                    try
                    {
                        has = _recoveryContext!.Store.HasAny(account, _operation);
                    }
                    catch (InvalidDataException)
                    {
                    }
                }
            }
        }
        _hasOutstandingName.Value = has;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged -= RefreshIdentity;
        foreach (AiRequestRecoveryLease claim in _claims.Values)
            claim.Dispose();
        _claims.Clear();
        _hasOutstandingName.Dispose();
    }

    /// <summary>
    /// The key for a request identified by <paramref name="parts"/>. The server
    /// holds a key to printable ASCII and to 255 characters, which this is.
    /// </summary>
    public string For(params ReadOnlySpan<string?> parts) => NameFor(parts).Key;

    /// <summary>
    /// The key for a request identified by <paramref name="parts"/>, and
    /// whether it has been handed out since the last settlement.
    /// </summary>
    public AiRequestName NameFor(params ReadOnlySpan<string?> parts)
    {
        string operation = ResolveOperation(parts);
        return Remember(Fingerprint(parts), operation, ResolveModel(operation, parts));
    }

    /// <summary>
    /// Names and durably records a form request in one call. The key-only
    /// overload remains useful for non-form callers, while the dialog VMs use
    /// this overload so a crash cannot leave a paid key without its inputs.
    /// </summary>
    internal AiRequestName NameFor(
        ReadOnlySpan<string?> parts,
        AiRequestFormSnapshot form,
        IReadOnlyList<AiRequestRecoverySource>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        string operation = ResolveOperation(parts);
        return Remember(
            Fingerprint(parts),
            operation,
            ResolveModel(operation, parts),
            form,
            sources);
    }

    /// <summary>
    /// The key for the <paramref name="pieceIndex"/>th piece of a request sent
    /// in pieces, each of which is charged and recovered on its own.
    /// </summary>
    public string For(int pieceIndex, params ReadOnlySpan<string?> parts)
        => NameFor(pieceIndex, parts).Key;

    public AiRequestName NameFor(int pieceIndex, params ReadOnlySpan<string?> parts)
    {
        string operation = ResolveOperation(parts);
        return Remember(
            $"{Fingerprint(parts)}-{pieceIndex.ToString(CultureInfo.InvariantCulture)}",
            operation,
            ResolveModel(operation, parts));
    }

    public bool HasPersistedFor(AiOperationId operation)
        => _recoveryContext?.TryGetIdentity() is { } identity
            && _recoveryContext.Store.HasAny(identity.AccountId, operation.Value);

    public IReadOnlyList<AiModelId> PersistedModels(AiOperationId operation)
        => _recoveryContext?.TryGetIdentity() is { } identity
            ? _recoveryContext.Store.ModelsFor(identity.AccountId, operation.Value)
                .Select(model => new AiModelId(model))
                .ToArray()
            : [];

    public AiModelId? PreferredPersistedModel(AiOperationId operation)
    {
        if (_recoveryContext?.TryGetIdentity() is not { } identity)
            return null;
        IReadOnlyList<AiPendingAttempt> attempts = _recoveryContext.Store.PendingFor(
            identity.AccountId,
            operation.Value);
        // A preferred model is safe only when there is exactly one pending
        // attempt and that attempt explicitly named a model. Returning a model
        // from one of several rows would bind a different form to it.
        if (attempts.Count != 1 || attempts[0].Model is null)
            return null;
        return attempts[0].Model is { } model ? new AiModelId(model) : null;
    }

    public bool HasExplicitNullPersistedModel(AiOperationId operation)
        => _recoveryContext?.TryGetIdentity() is { } identity
            && _recoveryContext.Store.PendingFor(identity.AccountId, operation.Value)
                .Count == 1
            && _recoveryContext.Store.HasModelless(identity.AccountId, operation.Value);

    internal IReadOnlyList<AiPendingAttempt> PendingAttempts(AiOperationId operation)
        => _recoveryContext?.PendingFor(operation.Value)
            ?? Array.Empty<AiPendingAttempt>();

    internal IReadOnlyList<string> ResolveSources(AiPendingAttempt attempt)
    {
        if (_recoveryContext is null)
            throw new InvalidDataException("AI request recovery is not configured.");
        return _recoveryContext.Store.ResolveSources(attempt);
    }

    internal byte[] ReadSourceBytes(AiRequestRecoverySource source)
    {
        if (_recoveryContext is null)
            throw new InvalidDataException("AI request recovery is not configured.");
        return _recoveryContext.Store.ReadSourceBytes(source);
    }

    internal AiRequestRecoverySource CreateDurableSource(
        string role,
        string name,
        ReadOnlySpan<byte> content,
        string? elementId = null)
    {
        if (_recoveryContext is null)
            throw new InvalidDataException("AI request recovery is not configured.");
        return _recoveryContext.Store.CreateDurableSource(role, name, content, elementId);
    }

    internal void CleanupUncommittedSources(IEnumerable<AiRequestRecoverySource> sources)
    {
        if (_recoveryContext is not null)
            _recoveryContext.Store.DeleteUncommittedSources(sources);
    }

    internal AiRequestRecoveryLease? TryClaim(AiRequestName name)
    {
        if (_recoveryContext is null || string.IsNullOrEmpty(name.Key))
            return null;
        lock (_gate)
        {
            if (!_outstanding.TryGetValue(name.Key, out string? request)
                || !_outstandingAccounts.TryGetValue(name.Key, out string? account)
                || !_outstandingOperations.TryGetValue(name.Key, out string? operation))
                return null;
            AiPendingAttempt? current = _recoveryContext.Store.Find(account, operation, request);
            if (current is null || !StringComparer.Ordinal.Equals(current.Key, name.Key))
                throw new InvalidDataException("AI request recovery attempt is stale.");

            // An unknown result leaves the dispatched fence durable so another
            // process cannot overlap the provider call. Keep this process's
            // exact owner on an immediate retry instead of asking the store for
            // a competing claim that it must (correctly) reject.
            if (_claims.GetValueOrDefault(name.Key) is { } localClaim
                && localClaim.WasDispatched)
            {
                if (localClaim.Reacquire())
                    return localClaim;

                // The old owner may have expired and been replaced after a
                // process restart. Drop only this local handle, then let the
                // store's owner-token CAS decide whether a fresh claim is safe.
                _claims.Remove(name.Key);
            }

            AiRequestRecoveryLease? claim = _recoveryContext.Store.Claim(account, operation, request, name.Key);
            if (claim is null)
                return null;
            _claims[name.Key] = claim;
            return claim;
        }
    }

    internal void MarkClaimDispatched(AiRequestRecoveryLease? claim)
    {
        if (claim is not null && !claim.MarkDispatched())
            throw new InvalidDataException("AI recovery dispatch fence could not be persisted.");
    }

    internal AiPendingAttempt? FindPending(
        AiOperationId operation,
        ReadOnlySpan<string?> parts)
    {
        if (_recoveryContext?.TryGetIdentity() is not { } identity)
            return null;
        return _recoveryContext.Store.Find(
            identity.AccountId,
            ResolveOperation(parts),
            Fingerprint(parts));
    }

    internal bool MatchesPending(AiPendingAttempt attempt, ReadOnlySpan<string?> parts)
        => _recoveryContext?.TryGetIdentity() is { } identity
            && StringComparer.Ordinal.Equals(identity.AccountId, attempt.AccountId)
            && string.Equals(
                attempt.Fingerprint,
                Fingerprint(parts),
                StringComparison.Ordinal)
            && string.Equals(attempt.Operation, ResolveOperation(parts), StringComparison.Ordinal);

    internal bool IsCurrentPending(AiPendingAttempt attempt)
    {
        if (_recoveryContext?.TryGetIdentity() is not { } identity
            || !StringComparer.Ordinal.Equals(identity.AccountId, attempt.AccountId))
        {
            return false;
        }

        AiPendingAttempt? current = _recoveryContext.Store.Find(
            attempt.AccountId,
            attempt.Operation,
            attempt.Fingerprint);
        return current is not null
            && StringComparer.Ordinal.Equals(current.Key, attempt.Key);
    }

    internal void PersistForm(
        AiRequestName name,
        AiRequestFormSnapshot form,
        IReadOnlyList<AiRequestRecoverySource>? sources = null)
    {
        if (_recoveryContext is null || string.IsNullOrEmpty(name.Key))
            return;
        ArgumentNullException.ThrowIfNull(form);
        lock (_gate)
        {
            if (!_outstanding.TryGetValue(name.Key, out string? request)
                || !_outstandingAccounts.TryGetValue(name.Key, out string? account)
                || !_outstandingOperations.TryGetValue(name.Key, out string? operation))
            {
                throw new InvalidOperationException("The AI request name is not outstanding.");
            }

            AiPendingAttempt? existing = _recoveryContext.Store.Find(account, operation, request);
            if (existing is null)
                throw new InvalidDataException("The AI recovery row is missing.");
            if (!_recoveryContext.Store.TryUpdateForm(
                    account,
                    operation,
                    request,
                    existing.Key,
                    form,
                    sources))
            {
                throw new InvalidDataException("The AI recovery row changed while updating form state.");
            }
        }
    }

    internal void Abandon(AiPendingAttempt attempt)
    {
        if (_recoveryContext is null)
            return;
        if (!StringComparer.Ordinal.Equals(CurrentAccountId, attempt.AccountId))
            throw new AuthenticationRequiredException();
        lock (_gate)
        {
            // Resolve the exact issued entry, including account and operation.
            // A fingerprint is scoped to both, so matching it alone can remove
            // another account's pending attempt after an account switch.
            string? key = null;
            foreach (KeyValuePair<string, string> pair in _outstanding)
            {
                if (pair.Value == attempt.Fingerprint
                    && _outstandingAccounts.GetValueOrDefault(pair.Key) == attempt.AccountId
                    && _outstandingOperations.GetValueOrDefault(pair.Key) == attempt.Operation)
                {
                    key = pair.Key;
                    break;
                }
            }
            AiPendingAttempt? persisted = _recoveryContext.Store.Find(
                attempt.AccountId,
                attempt.Operation,
                attempt.Fingerprint);
            if (persisted is not null
                && !StringComparer.Ordinal.Equals(persisted.Key, attempt.Key))
            {
                throw new InvalidDataException("AI recovery key does not match the pending attempt.");
            }
            bool removed = persisted is null && key is not null;
            if (persisted is not null)
            {
                BeforeAbandonPersistedRemoval?.Invoke();
                removed = _recoveryContext.Abandon(attempt);
                if (!removed)
                {
                    AiPendingAttempt? current = _recoveryContext.Store.Find(
                        attempt.AccountId,
                        attempt.Operation,
                        attempt.Fingerprint);
                    if (current is null)
                    {
                        removed = true;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            StringComparer.Ordinal.Equals(current.Key, attempt.Key)
                                ? "The AI recovery attempt has an active dispatch fence and cannot be abandoned."
                                : "The AI recovery attempt changed while it was being abandoned.");
                    }
                }
            }
            if (removed)
            {
                string generationIdentity =
                    $"{attempt.AccountId}\n{attempt.Operation}\n{attempt.Fingerprint}";
                int durableGeneration = persisted is null
                    ? _recoveryContext.Store.AdvanceGeneration(
                        attempt.AccountId,
                        attempt.Operation,
                        attempt.Fingerprint)
                    : _recoveryContext.Store.GetGeneration(
                        attempt.AccountId,
                        attempt.Operation,
                        attempt.Fingerprint);
                _generations[generationIdentity] = Math.Max(
                    _generations.GetValueOrDefault(generationIdentity) + 1,
                    durableGeneration);
                if (key is not null)
                {
                    _outstanding.Remove(key);
                    _outstandingAccounts.Remove(key);
                    _outstandingOperations.Remove(key);
                }
            }
        }
        RefreshIdentity();
    }

    public IDisposable EnterAuthenticatedScope(AiRequestName name)
    {
        if (_recoveryContext is null)
            return EmptyDisposable.Instance;
        string account;
        lock (_gate)
        {
            account = _outstandingAccounts.GetValueOrDefault(name.Key)
                ?? throw new InvalidOperationException("The AI request name is not outstanding.");
        }
        return _recoveryContext.Enter(account);
    }

    /// <summary>
    /// Identifies a file the way the server does: by the name it arrives under
    /// and by its bytes.
    /// </summary>
    /// <remarks>
    /// Anything else drifts from what the server calls the same request. Its
    /// path would make a file moved between folders a new request, and its
    /// modified time would do the same for a file merely touched — both would
    /// buy the same work a second time. Reading the bytes is what the upload is
    /// about to do anyway.
    /// </remarks>
    public static string FileStamp(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            return FileStamp(Path.GetFileName(filePath), SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable here means the upload is about to fail anyway; the path
            // alone still tells two different files apart.
            return filePath;
        }
    }

    /// <summary>
    /// Identifies bytes already in hand, sent under <paramref name="fileName"/>.
    /// </summary>
    /// <remarks>
    /// Reading the file once and naming that reading is the only way the name
    /// is sure to belong to what goes out: read for the name and read again to
    /// send, and a file rewritten in between is recorded under a name that
    /// describes something else.
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
    /// A video's frames are identified by their contents and their type, never
    /// by what the file was called: a frame is a picture, and the name it
    /// reached disk under says nothing about the request. Naming it here would
    /// make the same picture two requests — a frame captured from the scene
    /// lands in a file named for uniqueness, so capturing it again to retry an
    /// interrupted run would buy that run a second time.
    /// </remarks>
    public static string ContentStamp(ReadOnlySpan<byte> content)
        => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>
    /// Says the server settled the job this one name made. Only that request
    /// starts again under a fresh name.
    /// </summary>
    /// <remarks>
    /// Settling the whole run instead would throw away the names of every other
    /// request still waiting to be collected: a run that finishes a second
    /// request while a first one is still outstanding would leave the first
    /// unreachable, and asking for it again would buy it a second time.
    /// </remarks>
    public bool Retire(AiRequestName name)
    {
        if (string.IsNullOrEmpty(name.Key))
            return false;

        bool anyLeft;
        bool retired = false;
        lock (_gate)
        {
            if (_outstanding.Remove(name.Key, out string? request))
            {
                string? issuedAccount = _outstandingAccounts.GetValueOrDefault(name.Key);
                string? issuedOperation = _outstandingOperations.GetValueOrDefault(name.Key);
                AiRequestRecoveryLease? claim = _claims.GetValueOrDefault(name.Key);
                string generationIdentity = _recoveryContext is null
                    ? request
                    : $"{issuedAccount}\n{issuedOperation}\n{request}";
                bool removedPersisted = RemovePersisted(
                    request,
                    issuedAccount,
                    issuedOperation,
                    name.Key,
                    settle: true,
                    claim?.OwnerToken,
                    claim?.Generation);
                if (!removedPersisted && _recoveryContext is not null)
                {
                    // Another process already changed the row. Do not let a
                    // stale local completion advance or delete that owner.
                    _outstanding[name.Key] = request;
                    if (issuedAccount is not null)
                        _outstandingAccounts[name.Key] = issuedAccount;
                    if (issuedOperation is not null)
                        _outstandingOperations[name.Key] = issuedOperation;
                    return false;
                }
                _generations[generationIdentity] =
                    _generations.GetValueOrDefault(generationIdentity) + 1;
                _outstandingAccounts.Remove(name.Key);
                _outstandingOperations.Remove(name.Key);
                claim?.Dispose();
                _claims.Remove(name.Key);
                retired = true;
            }

            anyLeft = HasCurrentOutstandingInMemory() || HasDurableOutstanding();
        }

        if (!anyLeft)
            _hasOutstandingName.Value = false;
        return retired;
    }

    /// <summary>
    /// Says every request of this run is settled, and the next one starts under
    /// a seed of its own.
    /// </summary>
    public bool Retire()
    {
        bool retired;
        lock (_gate)
        {
            if (_recoveryContext is not null)
            {
                if (_recoveryContext.TryGetIdentity() is not { } identity
                    || string.IsNullOrWhiteSpace(_operation))
                    return false;
                retired = _recoveryContext.Store.SettleMany(
                    identity.AccountId,
                    _operation,
                    _outstanding.Select(pair =>
                        new AiPendingAttempt(
                            _outstandingAccounts[pair.Key],
                            _outstandingOperations[pair.Key],
                            pair.Value,
                            pair.Key)))
                    || (_outstanding.Count == 0 && !HasDurableOutstanding());
            }
            else
            {
                retired = true;
            }
            if (!retired)
                return false;
            _seed = NewSeed();
            _outstanding.Clear();
            _outstandingAccounts.Clear();
            _outstandingOperations.Clear();
            _generations.Clear();
            // Nothing under the new seed has been paid for.
            _resumedAttemptPending = false;
        }

        _hasOutstandingName.Value = false;
        return true;
    }

    /// <summary>
    /// Forgets a name the server made no job under, so it stops counting as
    /// outstanding and the same request may go out under it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name is handed out before the request goes out, and some refusals
    /// happen before anything is reserved: the client's own balance check, and
    /// the server's answers about the plan, the sign-in, the size of an upload
    /// and whether a model can serve the request at all — every one of which it
    /// decides after looking the name up and finding nothing. Leaving such a
    /// name outstanding pins the run to the model it named and keeps the way
    /// back to a job open that was never made.
    /// </para>
    /// <para>
    /// Call this only where the server has been heard to reserve nothing, or
    /// where the request never left. A name withdrawn while the job it made is
    /// still out there is a job that will be bought again.
    /// </para>
    /// </remarks>
    public bool Withdraw(AiRequestName name)
        => WithdrawCore(name, ownerAuthorizedDispatched: false);

    /// <summary>
    /// Withdraws a name after the server authoritatively reported that no
    /// reservation was made, including when the provider call had already
    /// been marked dispatched.
    /// </summary>
    /// <remarks>
    /// A dispatched recovery row is a paid-job fence. Only the process that
    /// still holds the exact live dispatch lease may use this path; ordinary
    /// withdrawal and cross-process abandon remain fail-closed.
    /// </remarks>
    internal bool WithdrawAfterNoReservation(AiRequestName name)
        => WithdrawCore(name, ownerAuthorizedDispatched: true);

    private bool WithdrawCore(AiRequestName name, bool ownerAuthorizedDispatched)
    {
        if (string.IsNullOrEmpty(name.Key))
            return false;

        bool anyLeft;
        bool withdrawn = false;
        lock (_gate)
        {
            if (_outstanding.Remove(name.Key, out string? request))
            {
                string? issuedAccount = _outstandingAccounts.GetValueOrDefault(name.Key);
                string? issuedOperation = _outstandingOperations.GetValueOrDefault(name.Key);
                AiRequestRecoveryLease? claim = _claims.GetValueOrDefault(name.Key);
                bool exactDispatchedOwner = ownerAuthorizedDispatched
                    && claim is { IsDispatched: true };
                // A pre-dispatch refusal may still be withdrawn by the claim
                // owner. Once dispatch is persisted, however, the owner proof
                // is accepted only through WithdrawAfterNoReservation.
                bool exactPredispatchOwner = claim is not null
                    && !claim.WasDispatched;
                bool authorizedOwner = exactDispatchedOwner || exactPredispatchOwner;
                bool removedPersisted = RemovePersisted(
                    request,
                    issuedAccount,
                    issuedOperation,
                    name.Key,
                    settle: false,
                    authorizedOwner ? claim!.OwnerToken : null,
                    authorizedOwner ? claim!.Generation : null,
                    exactDispatchedOwner);
                if (!removedPersisted && _recoveryContext is not null)
                {
                    _outstanding[name.Key] = request;
                    if (issuedAccount is not null)
                        _outstandingAccounts[name.Key] = issuedAccount;
                    if (issuedOperation is not null)
                        _outstandingOperations[name.Key] = issuedOperation;
                    return false;
                }
                _outstandingAccounts.Remove(name.Key);
                _outstandingOperations.Remove(name.Key);
                claim?.Dispose();
                _claims.Remove(name.Key);
                withdrawn = true;
            }
            anyLeft = HasCurrentOutstandingInMemory() || HasDurableOutstanding();
        }

        if (!anyLeft)
            _hasOutstandingName.Value = false;
        return withdrawn;
    }

    private AiRequestName Remember(
        string request,
        string operation,
        string? model,
        AiRequestFormSnapshot? form = null,
        IReadOnlyList<AiRequestRecoverySource>? sources = null)
    {
        AiAuthenticatedRequestIdentity? authenticated = _recoveryContext?.GetRequiredIdentity();
        string? account = authenticated?.AccountId;
        AiRequestName name;
        bool hasOutstanding;
        lock (_gate)
        {
            if (_recoveryContext is not null)
            {
                if (operation.Length == 0)
                    throw new InvalidOperationException("An AI request operation is required for durable recovery.");
                try
                {
                    AiPendingAttempt? persisted = _recoveryContext.Store.Find(account!, operation, request);
                    if (persisted is not null)
                    {
                        string persistedKey = persisted.Key;
                        string[] staleKeys = _outstanding
                            .Where(pair => pair.Key != persistedKey
                                && pair.Value == request
                                && _outstandingAccounts.GetValueOrDefault(pair.Key) == account
                                && _outstandingOperations.GetValueOrDefault(pair.Key) == operation)
                            .Select(static pair => pair.Key)
                            .ToArray();
                        foreach (string staleKey in staleKeys)
                        {
                            _outstanding.Remove(staleKey);
                            _outstandingAccounts.Remove(staleKey);
                            _outstandingOperations.Remove(staleKey);
                            if (_claims.Remove(staleKey, out AiRequestRecoveryLease? staleClaim))
                                staleClaim.Dispose();
                        }
                        if (!_outstanding.ContainsKey(persistedKey))
                        {
                            _outstanding[persistedKey] = request;
                            _outstandingAccounts[persistedKey] = account!;
                            _outstandingOperations[persistedKey] = operation;
                        }
                        if (form is not null && !persisted.HasCanonicalForm)
                        {
                            _recoveryContext.Store.WriteOrGet(
                                persisted with { Form = form, Sources = sources });
                        }
                        else if (sources is not null)
                        {
                            // The caller may have prepared fresh durable copies
                            // while racing with another request that committed the
                            // same identity. Keep the committed row's copies and
                            // remove only the newly unreferenced ones.
                            _recoveryContext.Store.DeleteUncommittedSources(sources);
                        }
                        _hasOutstandingName.Value = true;
                        return new AiRequestName(persistedKey, true);
                    }
                }
                catch
                {
                    if (sources is not null)
                        _recoveryContext.Store.DeleteUncommittedSources(sources);
                    throw;
                }
            }
            string generationIdentity = _recoveryContext is null
                ? request
                : $"{account}\n{operation}\n{request}";
            int generation = _generations.GetValueOrDefault(generationIdentity);
            if (_recoveryContext is not null)
            {
                generation = Math.Max(
                    generation,
                    _recoveryContext.Store.GetGeneration(account!, operation, request));
            }
            // 第 0 世代の名前の形は、世代という考えが無かった頃と同じ。控えに
            // 残っている seed と、サーバーに残っている名前の両方が、そのまま
            // 通じる。
            string keyBase = _recoveryContext is null
                ? $"{_seed}-{request}"
                : $"{_seed}-{request}-{ScopeFingerprint(account!, operation)}";
            string key = generation == 0
                ? keyBase
                : $"{keyBase}-r{generation.ToString(CultureInfo.InvariantCulture)}";
            if (_outstanding.ContainsKey(key))
                return new AiRequestName(key, true);
            bool resumed = _resumedAttemptPending;
            _resumedAttemptPending = false;
            name = new AiRequestName(key, resumed);
            // Durable state is committed before exposing the name in memory.
            if (_recoveryContext is not null)
            {
                string persistedKey;
                try
                {
                    persistedKey = _recoveryContext.Store.WriteOrGet(
                        new AiPendingAttempt(account!, operation, request, key, model, form, sources)).Key;
                }
                catch
                {
                    if (sources is not null)
                        _recoveryContext.Store.DeleteUncommittedSources(sources);
                    throw;
                }
                if (!string.Equals(persistedKey, key, StringComparison.Ordinal))
                {
                    key = persistedKey;
                    name = new AiRequestName(key, true);
                }
            }
            _outstanding[key] = request;
            if (account is not null)
                _outstandingAccounts[key] = account;
            _outstandingOperations[key] = operation;
            hasOutstanding = true;
        }
        if (hasOutstanding)
            _hasOutstandingName.Value = true;
        return name;
    }

    private bool RemovePersisted(
        string request,
        string? issuedAccount,
        string? issuedOperation,
        string key,
        bool settle,
        string? ownerToken = null,
        int? generation = null,
        bool ownerAuthorizedDispatched = false)
    {
        if (_recoveryContext is null)
            return true;
        string? account = issuedAccount ?? _recoveryContext.TryGetIdentity()?.AccountId;
        string operation = issuedOperation ?? _operation
            ?? throw new InvalidOperationException("An AI request operation is required for durable recovery.");
        if (account is null)
            return false;
        bool removed = settle
            ? _recoveryContext.Store.TrySettle(account, operation, request, key, ownerToken, generation)
            : ownerAuthorizedDispatched
                ? _recoveryContext.Store.TryWithdrawAfterNoReservation(
                    account,
                    operation,
                    request,
                    key,
                    ownerToken ?? throw new InvalidOperationException("A dispatched withdrawal requires an owner token."),
                    generation ?? throw new InvalidOperationException("A dispatched withdrawal requires a generation."))
                : _recoveryContext.Store.TryWithdraw(account, operation, request, key, ownerToken, generation);
        return removed;
    }

    private bool HasDurableOutstanding()
        => _recoveryContext?.TryGetIdentity() is { } identity
            && !string.IsNullOrWhiteSpace(_operation)
            && _recoveryContext.Store.HasAny(identity.AccountId, _operation);

    private bool HasCurrentOutstandingInMemory()
    {
        string? account = _recoveryContext?.TryGetIdentity()?.AccountId;
        return account is null
            ? _outstanding.Count > 0
            : _outstandingAccounts.Values.Any(value => value == account);
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
            "video.generate" => 6,
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
}
