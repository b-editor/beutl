using System.Text;
using System.Text.Json;

namespace Beutl.Agents;

public sealed partial class AgentThreadStore
{
    public async Task<IReadOnlyList<AgentThreadSearchHit>> SearchThreadsAsync(
        string projectPath, string query, bool includeArchived, CancellationToken cancellationToken = default)
    {
        string term = OneLine(query);
        var threads = await ListAsync(projectPath, cancellationToken).ConfigureAwait(false);
        string directory = ProjectDirectory(projectPath);
        var matches = new List<AgentThreadSearchHit>();
        foreach (var thread in threads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (thread.IsArchived && !includeArchived)
                continue;
            if (term.Length == 0)
            {
                matches.Add(new(thread, null));
                continue;
            }

            // Release the project lock between transcripts so active threads can keep writing.
            using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
            string? snippet = await FindSnippetAsync(TimelinePath(directory, thread.Id), term, cancellationToken).ConfigureAwait(false);
            if (snippet is not null || thread.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                matches.Add(new(thread, snippet));
        }
        return matches;
    }

    private static async Task<string?> FindSnippetAsync(string path, string term, CancellationToken cancellationToken)
    {
        var streaming = new Dictionary<(string? Turn, string? Parent, string? Message), StringBuilder>();
        await foreach (var line in ReadTimelineLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            switch (Property(entry, "type"))
            {
                case "assistantTextDelta":
                {
                    var key = (Property(entry, "turnId"), Property(entry, "parentToolCallId"), Property(entry, "messageId"));
                    if (!streaming.TryGetValue(key, out var message))
                        streaming[key] = message = new StringBuilder();
                    message.Append(Property(entry, "text"));
                    break;
                }
                case "assistantMessageCompleted":
                    streaming.Remove((Property(entry, "turnId"), Property(entry, "parentToolCallId"), Property(entry, "messageId")));
                    goto case "userMessage";
                case "userMessage":
                    if (MakeSnippet(Property(entry, "text"), term) is { } snippet)
                        return snippet;
                    break;
                case "turnCompleted" or "turnFailed" or "turnCancelled":
                    foreach (var key in streaming.Keys.Where(key => key.Turn == Property(entry, "turnId")).ToArray())
                    {
                        if (MakeSnippet(streaming[key].ToString(), term) is { } partial)
                            return partial;
                        streaming.Remove(key);
                    }
                    break;
            }
        }
        return streaming.Values.Select(value => MakeSnippet(value.ToString(), term)).FirstOrDefault(value => value is not null);
    }

    private static string? Property(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string OneLine(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? MakeSnippet(string? text, string term)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string line = OneLine(text);
        int match = line.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (match < 0)
            return null;

        int start = Math.Max(0, match - 40);
        if (char.IsLowSurrogate(line[start]))
            start--;
        int end = Math.Min(line.Length, start + 128);
        if (char.IsHighSurrogate(line[end - 1]))
            end--;
        return (start > 0 ? "…" : "") + line[start..end].Trim() + (end < line.Length ? "…" : "");
    }
}
