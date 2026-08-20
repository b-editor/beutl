using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Reactive.Bindings;

namespace Beutl.Services.AI;

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
/// request is passed to <see cref="For(ReadOnlySpan{string})"/>.
/// </para>
/// <para>
/// Once the server has settled a job — a result, or a failure it owns and has
/// refunded — the key that named it is spent. It would keep answering with that
/// settled job, so asking for anything more has to be a new request:
/// <see cref="Retire"/> says the settlement happened and starts the next one
/// under fresh keys.
/// </para>
/// </remarks>
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

internal sealed class AiRequestKey
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);
    private readonly ReactivePropertySlim<bool> _hasOutstandingName = new(false);
    private string _seed;
    private bool _resumedAttemptPending;

    public AiRequestKey(string? seed = null)
    {
        // A run picked up from a draft has names outstanding before it hands one
        // out, so the way back to what it may have paid for is open from the
        // start.
        _hasOutstandingName.Value = !string.IsNullOrEmpty(seed);
        _seed = string.IsNullOrEmpty(seed) ? NewSeed() : seed;
        // A run picked up from a draft carries its names but not the record of
        // which of them have been sent, so the first one it asks for again may
        // already name a job it paid for before the session ended. It is
        // treated as a repeat once, and only once.
        _resumedAttemptPending = !string.IsNullOrEmpty(seed);
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
    /// whether it has been handed out since the last <see cref="Retire"/>.
    /// </summary>
    public AiRequestName NameFor(params ReadOnlySpan<string?> parts)
        => Remember($"{Seed}-{Fingerprint(parts)}");

    /// <summary>
    /// The key for the <paramref name="pieceIndex"/>th piece of a request sent
    /// in pieces, each of which is charged and recovered on its own.
    /// </summary>
    public string For(int pieceIndex, params ReadOnlySpan<string?> parts)
        => NameFor(pieceIndex, parts).Key;

    public AiRequestName NameFor(int pieceIndex, params ReadOnlySpan<string?> parts)
        => Remember(
            $"{Seed}-{Fingerprint(parts)}-{pieceIndex.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// Identifies a file as it stands now. The server fingerprints an upload by
    /// its bytes, so a request naming a file that has since been edited is a
    /// different request and has to be named differently too.
    /// </summary>
    public static string FileStamp(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;

        try
        {
            var file = new FileInfo(filePath);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{filePath}:{file.Length}:{file.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable here means the upload is about to fail anyway; the path
            // alone still tells two different files apart.
            return filePath;
        }
    }

    public void Retire()
    {
        lock (_gate)
        {
            _seed = NewSeed();
            _issued.Clear();
            // Nothing under the new seed has been paid for.
            _resumedAttemptPending = false;
        }

        _hasOutstandingName.Value = false;
    }

    private AiRequestName Remember(string key)
    {
        AiRequestName name;
        lock (_gate)
        {
            bool issuedBefore = !_issued.Add(key);
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
