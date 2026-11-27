using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.Agents;

public sealed class AgentThreadStore(string? beutlHome = null)
{
    private readonly string _root = Path.Combine(
        Path.GetFullPath(beutlHome ?? Environment.GetEnvironmentVariable("BEUTL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl")),
        "agent-threads");

    public async Task<IReadOnlyList<AgentThreadMetadata>> ListAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        return threads.OrderByDescending(thread => thread.UpdatedAt).ToArray();
    }

    public async Task<AgentThreadMetadata?> GetAsync(string projectPath, Guid id, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        return threads.Find(thread => thread.Id == id);
    }

    public async Task<AgentThreadMetadata> CreateAsync(
        string projectPath, string providerId, string title, AgentPermissionMode mode, string? model = null,
        CancellationToken cancellationToken = default, string? reasoningEffort = null)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var thread = new AgentThreadMetadata(Guid.NewGuid(), providerId, title, now, now, mode, model, ReasoningEffort: reasoningEffort);
        threads.Add(thread);
        await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
        return thread;
    }

    public Task<AgentThreadMetadata> RenameAsync(string projectPath, Guid id, string title, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, id, thread => thread with { Title = title }, cancellationToken);

    public Task<AgentThreadMetadata> SetPermissionModeAsync(
        string projectPath, Guid id, AgentPermissionMode mode, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, id, thread => thread with { PermissionMode = mode }, cancellationToken);

    internal async Task<(AgentThreadMetadata Thread, AgentThreadModelChanged? Change)> SetModelAsync(
        string projectPath, Guid id, string? model, string? effort, CancellationToken cancellationToken)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        int index = FindThread(threads, id);
        var previous = threads[index];
        if (previous.Model == model && previous.ReasoningEffort == effort)
            return (previous, null);

        var now = DateTimeOffset.UtcNow;
        var thread = previous with { Model = model, ReasoningEffort = effort, UpdatedAt = now };
        var change = new AgentThreadModelChanged(id, null, now, thread.ProviderId, previous.Model, model, previous.ReasoningEffort, effort);
        threads[index] = thread;
        await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
        try
        {
            await AppendTimelineFileAsync(directory, change, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            threads[index] = previous;
            await WriteIndexAsync(directory, threads, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return (thread, change);
    }

    public async Task<AgentThreadMetadata> ForkThreadAsync(
        string projectPath, Guid id, Guid turnId, string title, bool includeTurn = true, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        var source = threads[FindThread(threads, id)];
        var entries = await ReadTimelinePrefixAsync(directory, id, turnId, includeTurn, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var fork = source with
        {
            Id = Guid.NewGuid(), Title = title, CreatedAt = now, UpdatedAt = now,
            ExternalSessionId = null, RestorePreviousMessages = entries.Any(entry => entry is AgentUserMessage)
        };

        string path = TimelinePath(directory, fork.Id);
        try
        {
            await WriteTimelineAsync(path, entries.Select(entry => ReassignThread(entry, fork.Id)), cancellationToken)
                .ConfigureAwait(false);
            threads.Add(fork);
            await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
            return fork;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public async Task<(AgentThreadMetadata Thread, IReadOnlyList<AgentTimelineEvent> Timeline)> RewindTurnAsync(
        string projectPath, Guid id, Guid turnId, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        int index = FindThread(threads, id);
        var entries = await ReadTimelinePrefixAsync(directory, id, turnId, includeTurn: false, cancellationToken)
            .ConfigureAwait(false);
        var thread = threads[index] with
        {
            UpdatedAt = DateTimeOffset.UtcNow, ExternalSessionId = null,
            RestorePreviousMessages = entries.Any(entry => entry is AgentUserMessage)
        };
        string path = TimelinePath(directory, id);
        string pending = path + ".rewinding";
        try
        {
            await WriteTimelineAsync(pending, entries, cancellationToken).ConfigureAwait(false);
            threads[index] = thread;
            // A crash between these moves can leave the old transcript, but never
            // a shortened transcript that still resumes the old provider session.
            await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
            File.Move(pending, path, overwrite: true);
            return (thread, entries);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    private static async Task<AgentTimelineEvent[]> ReadTimelinePrefixAsync(
        string directory, Guid id, Guid turnId, bool includeTurn, CancellationToken cancellationToken)
    {
        var entries = await ReadTimelineFileAsync(TimelinePath(directory, id), cancellationToken).ConfigureAwait(false);
        int start = entries.FindIndex(entry => entry.TurnId == turnId && entry is AgentTurnStarted or AgentUserMessage);
        if (start < 0)
            throw new KeyNotFoundException($"Agent turn {turnId} does not exist in thread {id}.");

        int end = includeTurn ? entries.FindLastIndex(entry => entry.TurnId == turnId) + 1 : start;
        return entries.Take(end)
            .Where(entry => entry is not AgentSessionStateChanged).ToArray();
    }

    private static AgentTimelineEvent ReassignThread(AgentTimelineEvent entry, Guid id) => entry switch
    {
        AgentPermissionRequested permission => permission with { ThreadId = id, Request = permission.Request with { ThreadId = id } },
        AgentUserQuestionRequested question => question with { ThreadId = id, Request = question.Request with { ThreadId = id } },
        _ => entry with { ThreadId = id }
    };

    private static async Task WriteTimelineAsync(
        string path, IEnumerable<AgentTimelineEvent> entries, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await JsonSerializer.SerializeAsync<AgentTimelineEvent>(stream, entry, AgentJson.Options, cancellationToken)
                .ConfigureAwait(false);
            stream.WriteByte((byte)'\n');
        }
    }

    private async Task<AgentThreadMetadata> UpdateAsync(
        string projectPath, Guid id, Func<AgentThreadMetadata, AgentThreadMetadata> update, CancellationToken cancellationToken)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        int index = FindThread(threads, id);
        var thread = update(threads[index]) with { UpdatedAt = DateTimeOffset.UtcNow };
        threads[index] = thread;
        await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
        return thread;
    }

    public async Task<bool> DeleteAsync(string projectPath, Guid id, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        if (threads.RemoveAll(thread => thread.Id == id) == 0)
            return false;

        await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
        File.Delete(TimelinePath(directory, id));
        return true;
    }

    public async Task AppendAsync(string projectPath, AgentTimelineEvent entry, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        int index = FindThread(threads, entry.ThreadId);
        var thread = threads[index];

        // Persist the resume ID before acknowledging the first event for a provider session.
        string? sessionId = (entry as AgentSessionStateChanged)?.ExternalSessionId;
        if (!string.IsNullOrEmpty(sessionId) && sessionId != thread.ExternalSessionId)
        {
            thread = thread with { ExternalSessionId = sessionId, UpdatedAt = LaterOf(thread.UpdatedAt, entry.Timestamp) };
            threads[index] = thread;
            await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
        }

        await AppendTimelineFileAsync(directory, entry, cancellationToken).ConfigureAwait(false);

        if (entry is AgentUserMessage or AgentTurnCompleted or AgentTurnFailed or AgentTurnCancelled)
        {
            threads[index] = thread with
            {
                UpdatedAt = LaterOf(thread.UpdatedAt, entry.Timestamp),
                RestorePreviousMessages = thread.RestorePreviousMessages && entry is not AgentUserMessage
            };
            await WriteIndexAsync(directory, threads, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task AppendTimelineFileAsync(string directory, AgentTimelineEvent entry, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(TimelinePath(directory, entry.ThreadId), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        RepairIncompleteLine(stream);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(entry, AgentJson.Options);
        cancellationToken.ThrowIfCancellationRequested();
        long start = stream.Position;
        try
        {
            await stream.WriteAsync(json).ConfigureAwait(false);
            stream.WriteByte((byte)'\n');
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            stream.SetLength(start);
            throw;
        }
    }

    public async Task<IReadOnlyList<AgentTimelineEvent>> ReadTimelineAsync(
        string projectPath, Guid id, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        return await ReadTimelineFileAsync(TimelinePath(directory, id), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<AgentTimelineEvent>> ReadTimelineFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        bool complete = stream.Length == 0 || stream.ReadByteAtEnd() == '\n';
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new List<AgentTimelineEvent>();
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        while (line != null)
        {
            string? next = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!complete && next == null)
                break;
            entries.Add(JsonSerializer.Deserialize<AgentTimelineEvent>(line, AgentJson.Options)
                ?? throw new JsonException("Expected a timeline event."));
            line = next;
        }
        return entries;
    }

    internal string ProjectDirectory(string projectPath)
    {
        string canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        if (OperatingSystem.IsWindows())
            canonicalPath = canonicalPath.ToUpperInvariant();

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
        return Path.Combine(_root, hash);
    }

    private static string TimelinePath(string directory, Guid id) => Path.Combine(directory, $"{id:N}.jsonl");

    private static DateTimeOffset LaterOf(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private static int FindThread(List<AgentThreadMetadata> threads, Guid id)
    {
        int index = threads.FindIndex(thread => thread.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException($"Agent thread {id} does not exist.");
    }

    internal static async Task<FileStream> LockAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ".lock");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<List<AgentThreadMetadata>> ReadIndexAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "index.json");
        if (!File.Exists(path))
            return [];
        using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<AgentThreadMetadata>>(stream, AgentJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new JsonException("Expected an agent thread index.");
    }

    private static async Task WriteIndexAsync(
        string directory, List<AgentThreadMetadata> threads, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "index.json");
        string pending = Path.Combine(directory, "index.pending");
        try
        {
            using (var stream = File.Create(pending))
            {
                await JsonSerializer.SerializeAsync(stream, threads, AgentJson.Options, cancellationToken).ConfigureAwait(false);
            }
            File.Move(pending, path, overwrite: true);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    private static void RepairIncompleteLine(FileStream stream)
    {
        if (stream.Length == 0 || stream.ReadByteAtEnd() == '\n')
            return;

        Span<byte> buffer = stackalloc byte[4096];
        long end = stream.Length;
        while (end > 0)
        {
            int count = (int)Math.Min(end, buffer.Length);
            long offset = end - count;
            stream.Position = offset;
            stream.ReadExactly(buffer[..count]);
            int lastNewline = buffer[..count].LastIndexOf((byte)'\n');
            if (lastNewline >= 0)
            {
                stream.Position = offset + lastNewline + 1;
                stream.SetLength(stream.Position);
                return;
            }
            end = offset;
        }
        stream.SetLength(0);
        stream.Position = 0;
    }
}

file static class TimelineStreamExtensions
{
    internal static int ReadByteAtEnd(this FileStream stream)
    {
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte();
    }
}
