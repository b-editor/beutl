using System.Text.Json;
using Beutl.IO;

namespace Beutl.Agents;

public sealed record AgentDraft(
    string? Prompt, string[] AttachmentPaths, bool IncludeSceneContext = true, string?[]? AttachmentMediaTypes = null,
    IReadOnlyList<AgentContextMention>? Mentions = null);

public sealed record AgentDraftEntry(AgentDraft? Draft, string[] History);

public sealed class AgentDraftStore(string? beutlHome = null)
{
    public const int HistoryLimit = 50;
    private readonly AgentThreadStore _threads = new(beutlHome);

    public async Task<AgentDraftEntry> LoadAsync(
        string projectPath, Guid? threadId, CancellationToken cancellationToken = default)
    {
        string directory = _threads.ProjectDirectory(projectPath);
        using var projectLock = await AgentThreadStore.LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var entries = await ReadAsync(Path.Combine(directory, "drafts.json"), cancellationToken).ConfigureAwait(false);
        return entries.GetValueOrDefault(Key(threadId)) ?? new(null, []);
    }

    public Task SaveAsync(string projectPath, Guid? threadId, AgentDraft? draft, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, threadId, entry => entry with { Draft = draft }, cancellationToken);

    public Task RecordPromptAsync(
        string projectPath, Guid threadId, string prompt, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, threadId, entry => entry.History.LastOrDefault() == prompt
            ? entry : entry with { History = [.. entry.History.TakeLast(HistoryLimit - 1), prompt] }, cancellationToken);

    public Task SeedHistoryAsync(
        string projectPath, Guid threadId, IReadOnlyList<string> history, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, threadId, entry => entry.History.Length > 0
            ? entry : entry with { History = [.. history.TakeLast(HistoryLimit)] }, cancellationToken);

    public Task DeleteAsync(string projectPath, Guid threadId, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, threadId, _ => new(null, []), cancellationToken);

    private static string Key(Guid? threadId) => threadId?.ToString("D") ?? "draft";

    private async Task UpdateAsync(
        string projectPath, Guid? threadId, Func<AgentDraftEntry, AgentDraftEntry> update, CancellationToken cancellationToken)
    {
        string directory = _threads.ProjectDirectory(projectPath);
        using var projectLock = await AgentThreadStore.LockAsync(directory, cancellationToken).ConfigureAwait(false);
        string path = Path.Combine(directory, "drafts.json");
        var entries = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        string key = Key(threadId);
        var previous = entries.GetValueOrDefault(key) ?? new(null, []);
        var entry = update(previous);
        if (entry == previous)
            return;
        if (entry.Draft is null && entry.History.Length == 0)
        {
            if (!entries.Remove(key))
                return;
        }
        else
        {
            entries[key] = entry;
        }

        using var output = new StagedOutputFile(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var stream = new FileStream(output.TemporaryPath, options))
            await JsonSerializer.SerializeAsync(stream, entries, AgentJson.Options, cancellationToken).ConfigureAwait(false);
        output.Commit(cancellationToken);
    }

    private static async Task<Dictionary<string, AgentDraftEntry>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, AgentDraftEntry>>(stream, AgentJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new JsonException("Expected agent drafts.");
    }
}
