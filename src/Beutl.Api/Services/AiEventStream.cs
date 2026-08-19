using System.Runtime.CompilerServices;
using System.Text;

namespace Beutl.Api.Services;

/// <summary>One event from a server-sent event stream.</summary>
internal readonly record struct AiServerSentEvent(string Event, string Data);

/// <summary>
/// Reads an answer that arrives a piece at a time.
/// </summary>
/// <remarks>
/// The AI endpoints stream when asked to: subtitles as they are translated, or
/// the rough versions of a picture as it is worked out, and then always one
/// closing event carrying the same answer a caller that did not ask would have
/// waited for. Refit cannot read that, so these requests are made with the
/// HttpClient directly.
/// </remarks>
internal static class AiEventStream
{
    public const string ResultEvent = "result";
    public const string ErrorEvent = "error";
    public const string MediaType = "text/event-stream";

    // A single event of a translated subtitle or a rough picture; the largest of
    // them carries a base64 image. Anything past this is not an event this
    // client knows how to use, and reading it would only cost memory.
    private const int MaximumEventLength = 32 * 1024 * 1024;

    public static bool IsEventStream(HttpResponseMessage response)
        => string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            MediaType,
            StringComparison.OrdinalIgnoreCase);

    public static async IAsyncEnumerable<AiServerSentEvent> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? name = null;
        var data = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;

            // A blank line ends an event. One with no name or no data is a
            // keep-alive comment or a field this client does not use.
            if (line.Length == 0)
            {
                if (name is not null && data.Length > 0)
                    yield return new AiServerSentEvent(name, data.ToString());
                name = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':'))
                continue;
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line["event:".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            if (data.Length > 0)
                data.Append('\n');
            data.Append(line["data:".Length..].TrimStart());
            if (data.Length > MaximumEventLength)
                throw new AiException("An AI event stream sent an oversized event.");
        }

        if (name is not null && data.Length > 0)
            yield return new AiServerSentEvent(name, data.ToString());
    }
}
