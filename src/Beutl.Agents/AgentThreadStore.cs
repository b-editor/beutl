namespace Beutl.Agents;

public sealed partial class AgentThreadStore(string? beutlHome = null)
{
    private readonly string _root = Path.Combine(
        Path.GetFullPath(beutlHome ?? Environment.GetEnvironmentVariable("BEUTL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl")),
        "agent-threads");

    public Task<IReadOnlyList<AgentThreadMetadata>> ListAsync(string projectPath, CancellationToken cancellationToken = default)
        => WithIndexLockAsync<IReadOnlyList<AgentThreadMetadata>>(projectPath, (_, threads) =>
            Task.FromResult<IReadOnlyList<AgentThreadMetadata>>(
                threads.OrderByDescending(thread => thread.IsPinned).ThenByDescending(thread => thread.UpdatedAt).ToArray()),
            cancellationToken);

    public Task<AgentThreadMetadata?> GetAsync(string projectPath, Guid id, CancellationToken cancellationToken = default)
        => WithIndexLockAsync(projectPath, (_, threads) => Task.FromResult(threads.Find(thread => thread.Id == id)), cancellationToken);

    public Task<AgentThreadMetadata> CreateAsync(
        string projectPath, string providerId, string title, AgentPermissionMode mode, string? model = null,
        CancellationToken cancellationToken = default, string? reasoningEffort = null)
        => WithIndexLockAsync(projectPath, async (directory, threads) =>
        {
            var now = DateTimeOffset.UtcNow;
            var thread = new AgentThreadMetadata(Guid.NewGuid(), providerId, title, now, now, mode, model, ReasoningEffort: reasoningEffort);
            threads.Add(thread);
            await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
            return thread;
        }, cancellationToken);

    public Task<AgentThreadMetadata> RenameAsync(string projectPath, Guid id, string title, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, id, thread => thread with { Title = title }, cancellationToken);

    public Task<AgentThreadMetadata> SetPermissionModeAsync(
        string projectPath, Guid id, AgentPermissionMode mode, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, id, thread => thread with { PermissionMode = mode }, cancellationToken);

    public Task<AgentThreadMetadata> SetPinnedAsync(
        string projectPath, Guid id, bool pinned, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, id, thread => thread with { IsPinned = pinned }, cancellationToken, touch: false);

    public Task<AgentThreadMetadata> SetArchivedAsync(
        string projectPath, Guid id, bool archived, CancellationToken cancellationToken = default)
        => UpdateAsync(projectPath, id, thread => thread with { IsArchived = archived }, cancellationToken, touch: false);

    public Task<AgentThreadMetadata> MarkReadAsync(
        string projectPath, Guid id, CancellationToken cancellationToken = default, DateTimeOffset? observedThrough = null)
        => UpdateAsync(projectPath, id, thread =>
        {
            var observed = observedThrough ?? LaterOf(DateTimeOffset.UtcNow, thread.LastActivityAt ?? DateTimeOffset.MinValue);
            return thread.LastReadAt >= observed ? thread : thread with { LastReadAt = observed };
        }, cancellationToken, touch: false);

    internal async Task<(AgentThreadMetadata Thread, AgentThreadModelChanged? Change)> SetModelAsync(
        string projectPath, Guid id, string? model, string? effort, CancellationToken cancellationToken)
    {
        var (thread, change) = await SetProviderOrModelAsync(projectPath, id, providerId: null, model, effort, cancellationToken)
            .ConfigureAwait(false);
        return (thread, (AgentThreadModelChanged?)change);
    }

    public Task<(AgentThreadMetadata Thread, AgentTimelineEvent? Change)> SetProviderAsync(
        string projectPath, Guid id, string providerId, string? model, string? effort, CancellationToken cancellationToken = default)
        => SetProviderOrModelAsync(projectPath, id, providerId, model, effort, cancellationToken);

    private Task<(AgentThreadMetadata Thread, AgentTimelineEvent? Change)> SetProviderOrModelAsync(
        string projectPath, Guid id, string? providerId, string? model, string? effort, CancellationToken cancellationToken)
        => WithIndexLockAsync<(AgentThreadMetadata, AgentTimelineEvent?)>(projectPath, async (directory, threads) =>
        {
            int index = FindThread(threads, id);
            var previous = threads[index];
            string effectiveProviderId = providerId ?? previous.ProviderId;
            bool providerChanged = previous.ProviderId != effectiveProviderId;
            if (!providerChanged && previous.Model == model && previous.ReasoningEffort == effort)
                return (previous, null);

            var now = DateTimeOffset.UtcNow;
            var thread = previous with { ProviderId = effectiveProviderId, Model = model, ReasoningEffort = effort, UpdatedAt = now };
            AgentTimelineEvent change;
            if (providerChanged)
            {
                thread = thread with { ExternalSessionId = null, RestorePreviousMessages = true };
                change = new AgentThreadProviderChanged(id, null, now, previous.ProviderId, effectiveProviderId);
            }
            else
            {
                change = new AgentThreadModelChanged(id, null, now, effectiveProviderId, previous.Model, model, previous.ReasoningEffort, effort);
            }
            await SaveWithMarkerAsync(directory, threads, index, thread, change, cancellationToken).ConfigureAwait(false);
            return (thread, change);
        }, cancellationToken);

    internal Task<AgentThreadMetadata> ResetContextAsync(
        string projectPath, AgentTimelineEvent marker, bool restorePreviousMessages, CancellationToken cancellationToken)
        => WithIndexLockAsync(projectPath, async (directory, threads) =>
        {
            int index = FindThread(threads, marker.ThreadId);
            var thread = threads[index] with
            {
                ExternalSessionId = null,
                RestorePreviousMessages = restorePreviousMessages,
                UpdatedAt = LaterOf(threads[index].UpdatedAt, marker.Timestamp)
            };
            await SaveWithMarkerAsync(directory, threads, index, thread, marker, cancellationToken).ConfigureAwait(false);
            return thread;
        }, cancellationToken);

    private static async Task SaveWithMarkerAsync(string directory, List<AgentThreadMetadata> threads, int index,
        AgentThreadMetadata thread, AgentTimelineEvent marker, CancellationToken cancellationToken)
    {
        var previous = threads[index];
        threads[index] = thread;
        await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
        try
        {
            await AppendTimelineFileAsync(directory, marker, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            threads[index] = previous;
            await WriteIndexAsync(directory, threads, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task<AgentThreadMetadata> ForkThreadAsync(
        string projectPath, Guid id, Guid? turnId, string title, bool includeTurn = true, CancellationToken cancellationToken = default)
        => WithIndexLockAsync(projectPath, async (directory, threads) =>
        {
            var source = threads[FindThread(threads, id)];
            var entries = await ReadTimelinePrefixAsync(directory, id, turnId, includeTurn, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var fork = source with
            {
                Id = Guid.NewGuid(),
                Title = title,
                CreatedAt = now,
                UpdatedAt = now,
                ExternalSessionId = null,
                RestorePreviousMessages = HasConversation(entries),
                IsPinned = false,
                IsArchived = false,
                LastReadAt = null,
                LastActivityAt = null
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
        }, cancellationToken);

    public Task<(AgentThreadMetadata Thread, IReadOnlyList<AgentTimelineEvent> Timeline)> RewindTurnAsync(
        string projectPath, Guid id, Guid turnId, CancellationToken cancellationToken = default)
        => WithIndexLockAsync<(AgentThreadMetadata, IReadOnlyList<AgentTimelineEvent>)>(projectPath, async (directory, threads) =>
        {
            int index = FindThread(threads, id);
            var entries = await ReadTimelinePrefixAsync(directory, id, turnId, includeTurn: false, cancellationToken)
                .ConfigureAwait(false);
            var thread = threads[index] with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                ExternalSessionId = null,
                RestorePreviousMessages = HasConversation(entries)
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
        }, cancellationToken);

    private static async Task<AgentTimelineEvent[]> ReadTimelinePrefixAsync(
        string directory, Guid id, Guid? turnId, bool includeTurn, CancellationToken cancellationToken)
    {
        var entries = await ReadTimelineFileAsync(TimelinePath(directory, id), cancellationToken).ConfigureAwait(false);
        if (turnId is null)
            return entries.Where(entry => entry is not AgentSessionStateChanged).ToArray();
        int start = entries.FindIndex(entry => entry.TurnId == turnId && entry is AgentTurnStarted or AgentUserMessage);
        if (start < 0)
            throw new KeyNotFoundException($"Agent turn {turnId} does not exist in thread {id}.");

        int end = includeTurn ? entries.FindLastIndex(entry => entry.TurnId == turnId) + 1 : start;
        return entries.Take(end)
            .Where(entry => entry is not AgentSessionStateChanged).ToArray();
    }

    private static bool HasConversation(AgentTimelineEvent[] entries)
        => entries.Reverse().TakeWhile(entry => entry is not AgentContextCleared)
            .Any(entry => entry is AgentUserMessage or AgentAssistantMessageCompleted { ParentToolCallId: null });

    private static AgentTimelineEvent ReassignThread(AgentTimelineEvent entry, Guid id) => entry switch
    {
        AgentPermissionRequested permission => permission with { ThreadId = id, Request = permission.Request with { ThreadId = id } },
        AgentUserQuestionRequested question => question with { ThreadId = id, Request = question.Request with { ThreadId = id } },
        _ => entry with { ThreadId = id }
    };

    private Task<AgentThreadMetadata> UpdateAsync(
        string projectPath, Guid id, Func<AgentThreadMetadata, AgentThreadMetadata> update,
        CancellationToken cancellationToken, bool touch = true)
        => WithIndexLockAsync(projectPath, async (directory, threads) =>
        {
            int index = FindThread(threads, id);
            var thread = update(threads[index]);
            if (!touch && thread == threads[index])
                return thread;
            if (touch)
                thread = thread with { UpdatedAt = DateTimeOffset.UtcNow };
            threads[index] = thread;
            await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
            return thread;
        }, cancellationToken);

    public Task<bool> DeleteAsync(string projectPath, Guid id, CancellationToken cancellationToken = default)
        => WithIndexLockAsync(projectPath, async (directory, threads) =>
        {
            if (threads.RemoveAll(thread => thread.Id == id) == 0)
                return false;

            await WriteIndexAsync(directory, threads, cancellationToken).ConfigureAwait(false);
            File.Delete(TimelinePath(directory, id));
            return true;
        }, cancellationToken);

    public Task AppendAsync(string projectPath, AgentTimelineEvent entry, CancellationToken cancellationToken = default)
        => WithIndexLockAsync(projectPath, async (directory, threads) =>
        {
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

            bool activity = entry is AgentTurnCompleted or AgentTurnFailed or AgentPermissionRequested or AgentUserQuestionRequested;
            if (activity || entry is AgentUserMessage or AgentTurnCancelled)
            {
                threads[index] = thread with
                {
                    UpdatedAt = LaterOf(thread.UpdatedAt, entry.Timestamp),
                    RestorePreviousMessages = thread.RestorePreviousMessages && entry is not AgentTurnCompleted,
                    LastActivityAt = activity ? LaterOf(thread.LastActivityAt ?? entry.Timestamp, entry.Timestamp) : thread.LastActivityAt
                };
                await WriteIndexAsync(directory, threads, CancellationToken.None).ConfigureAwait(false);
            }
        }, cancellationToken);

    public async Task<IReadOnlyList<AgentTimelineEvent>> ReadTimelineAsync(
        string projectPath, Guid id, CancellationToken cancellationToken = default)
    {
        string directory = ProjectDirectory(projectPath);
        await using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        return await ReadTimelineFileAsync(TimelinePath(directory, id), cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WithIndexLockAsync<T>(
        string projectPath, Func<string, List<AgentThreadMetadata>, Task<T>> action, CancellationToken cancellationToken)
    {
        string directory = ProjectDirectory(projectPath);
        await using var projectLock = await LockAsync(directory, cancellationToken).ConfigureAwait(false);
        var threads = await ReadIndexAsync(directory, cancellationToken).ConfigureAwait(false);
        return await action(directory, threads).ConfigureAwait(false);
    }

    private Task WithIndexLockAsync(
        string projectPath, Func<string, List<AgentThreadMetadata>, Task> action, CancellationToken cancellationToken)
        => WithIndexLockAsync<bool>(projectPath, async (directory, threads) =>
        {
            await action(directory, threads).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private static DateTimeOffset LaterOf(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private static int FindThread(List<AgentThreadMetadata> threads, Guid id)
    {
        int index = threads.FindIndex(thread => thread.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException($"Agent thread {id} does not exist.");
    }
}
