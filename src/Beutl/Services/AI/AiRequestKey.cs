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
        => exception is AuthenticationRequiredException
            or AiPlanRequiredException
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
internal sealed class AiRequestKey
{
    private readonly Lock _gate = new();
    // 出した名前と、それが何の依頼のものか。決着していないものだけが入っている。
    private readonly Dictionary<string, string> _outstanding = new(StringComparer.Ordinal);
    // 決着した依頼は、その名前ではもう何も頼めない。同じ依頼をもう一度出すときは
    // 次の世代の名前で出す——ここに何も無ければ第 0 世代で、名前の形は変わらない。
    private readonly Dictionary<string, int> _generations = new(StringComparer.Ordinal);
    private readonly ReactivePropertySlim<bool> _hasOutstandingName = new(false);
    private string _seed;
    private bool _resumedAttemptPending;

    public AiRequestKey(string? seed = null, bool namePending = false)
    {
        _seed = string.IsNullOrEmpty(seed) ? NewSeed() : seed;
        // 控えから拾い直した実行のうち、送った直後に終わったものだけが名前を
        // 抱えている。抱えていたかどうかは控えに書いてあり、seed があることでは
        // 分からない——予約されなかった依頼や、返金済みの依頼の seed も残る。
        bool resumed = !string.IsNullOrEmpty(seed) && namePending;
        _hasOutstandingName.Value = resumed;
        // 拾い直した実行は名前を持っていても「どれを送ったか」は持っていないので、
        // 最初に配り直す名前を一度だけ再送として扱う。
        _resumedAttemptPending = resumed;
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
    public IReadOnlyReactiveProperty<bool> HasOutstandingName => _hasOutstandingName;

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
        => Remember(Fingerprint(parts));

    /// <summary>
    /// The key for the <paramref name="pieceIndex"/>th piece of a request sent
    /// in pieces, each of which is charged and recovered on its own.
    /// </summary>
    public string For(int pieceIndex, params ReadOnlySpan<string?> parts)
        => NameFor(pieceIndex, parts).Key;

    public AiRequestName NameFor(int pieceIndex, params ReadOnlySpan<string?> parts)
        => Remember(
            $"{Fingerprint(parts)}-{pieceIndex.ToString(CultureInfo.InvariantCulture)}");

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
    /// Says the server settled the job this one name made. Only that request
    /// starts again under a fresh name.
    /// </summary>
    /// <remarks>
    /// Settling the whole run instead would throw away the names of every other
    /// request still waiting to be collected: a run that finishes a second
    /// request while a first one is still outstanding would leave the first
    /// unreachable, and asking for it again would buy it a second time.
    /// </remarks>
    public void Retire(AiRequestName name)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;

        bool anyLeft;
        lock (_gate)
        {
            if (_outstanding.Remove(name.Key, out string? request))
            {
                _generations[request] = _generations.GetValueOrDefault(request) + 1;
            }

            anyLeft = _outstanding.Count > 0;
        }

        if (!anyLeft)
            _hasOutstandingName.Value = false;
    }

    /// <summary>
    /// Says every request of this run is settled, and the next one starts under
    /// a seed of its own.
    /// </summary>
    public void Retire()
    {
        lock (_gate)
        {
            _seed = NewSeed();
            _outstanding.Clear();
            _generations.Clear();
            // Nothing under the new seed has been paid for.
            _resumedAttemptPending = false;
        }

        _hasOutstandingName.Value = false;
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
    public void Withdraw(AiRequestName name)
    {
        if (string.IsNullOrEmpty(name.Key))
            return;

        bool anyLeft;
        lock (_gate)
        {
            _outstanding.Remove(name.Key);
            anyLeft = _outstanding.Count > 0;
        }

        if (!anyLeft)
            _hasOutstandingName.Value = false;
    }

    private AiRequestName Remember(string request)
    {
        AiRequestName name;
        lock (_gate)
        {
            int generation = _generations.GetValueOrDefault(request);
            // 第 0 世代の名前の形は、世代という考えが無かった頃と同じ。控えに
            // 残っている seed と、サーバーに残っている名前の両方が、そのまま
            // 通じる。
            string key = generation == 0
                ? $"{_seed}-{request}"
                : $"{_seed}-{request}-r{generation.ToString(CultureInfo.InvariantCulture)}";
            bool issuedBefore = !_outstanding.TryAdd(key, request);
            if (issuedBefore)
            {
                name = new AiRequestName(key, true);
            }
            else
            {
                bool resumed = _resumedAttemptPending;
                _resumedAttemptPending = false;
                name = new AiRequestName(key, resumed);
            }
        }

        _hasOutstandingName.Value = true;
        return name;
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
}
