namespace Beutl.Agents;

public sealed partial class AgentRuntimeManager
{
    public Task<IReadOnlyList<AgentThreadMetadata>> ListThreadsAsync(string projectPath, CancellationToken cancellationToken = default)
        => _store.ListAsync(projectPath, cancellationToken);

    public Task<IReadOnlyList<AgentThreadSearchHit>> SearchThreadsAsync(
        string projectPath, string query, bool includeArchived, CancellationToken cancellationToken = default)
        => _store.SearchThreadsAsync(projectPath, query, includeArchived, cancellationToken);

    public Task<AgentThreadMetadata> SetPinnedAsync(
        string projectPath, Guid threadId, bool pinned, CancellationToken cancellationToken = default)
        => _store.SetPinnedAsync(projectPath, threadId, pinned, cancellationToken);

    public Task<AgentThreadMetadata> SetArchivedAsync(
        string projectPath, Guid threadId, bool archived, CancellationToken cancellationToken = default)
        => _store.SetArchivedAsync(projectPath, threadId, archived, cancellationToken);

    public Task<AgentThreadMetadata> MarkReadAsync(
        string projectPath, Guid threadId, CancellationToken cancellationToken = default, DateTimeOffset? observedThrough = null)
        => _store.MarkReadAsync(projectPath, threadId, cancellationToken, observedThrough);

    public Task<AgentThreadMetadata> CreateThreadAsync(
        string projectPath, AgentProviderDescriptor provider, string title, AgentPermissionMode mode = AgentPermissionMode.Auto,
        string? model = null, CancellationToken cancellationToken = default, string? reasoningEffort = null)
        => _store.CreateAsync(projectPath, provider.Id, title, mode, model, cancellationToken, reasoningEffort);

    public Task<(AgentThreadMetadata Thread, AgentThreadModelChanged? Change)> SetModelAsync(
        string projectPath, Guid threadId, string? model, string? effort, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: false, async (_, ct) =>
        {
            await FlushTimelineAsync().ConfigureAwait(false);
            return await _store.SetModelAsync(projectPath, threadId, model, effort, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<AgentThreadMetadata> RenameThreadAsync(
        string projectPath, Guid threadId, string title, CancellationToken cancellationToken = default)
        => _store.RenameAsync(projectPath, threadId, title, cancellationToken);

    public Task<(AgentThreadMetadata Thread, AgentTimelineEvent? Change)> SetProviderAsync(
        string projectPath, Guid threadId, string providerId, string? model, string? effort, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: false, async (session, ct) =>
        {
            await FlushTimelineAsync().ConfigureAwait(false);
            var current = await _store.GetAsync(projectPath, threadId, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Agent thread {threadId} does not exist.");
            if (current.ProviderId != providerId)
                await CloseIdleSessionAsync(session, threadId, ct).ConfigureAwait(false);
            var result = await _store.SetProviderAsync(projectPath, threadId, providerId, model, effort, ct).ConfigureAwait(false);
            if (result.Change is { } change)
                PublishTimeline(change, null);
            return result;
        }, cancellationToken);

    public Task<(AgentThreadMetadata Thread, AgentContextCleared Marker)> ClearContextAsync(
        string projectPath, Guid threadId, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: true, async (session, ct) =>
        {
            await CloseIdleSessionAsync(session, threadId, ct).ConfigureAwait(false);
            var marker = new AgentContextCleared(threadId, null, DateTimeOffset.UtcNow);
            var thread = await _store.ResetContextAsync(projectPath, marker, false, ct).ConfigureAwait(false);
            PublishTimeline(marker, null);
            return (thread, marker);
        }, cancellationToken);

    private async Task CloseIdleSessionAsync(Session session, Guid threadId, CancellationToken cancellationToken)
    {
        await _bridge.CloseSessionAsync(threadId, cancellationToken).ConfigureAwait(false);
        await FlushTimelineAsync().ConfigureAwait(false);
        session.RememberedDecisions.Clear();
    }

    public Task<IReadOnlyList<AgentTimelineEvent>> LoadTimelineAsync(
        string projectPath, Guid threadId, CancellationToken cancellationToken = default)
        => _store.ReadTimelineAsync(projectPath, threadId, cancellationToken);

    public Task<AgentThreadMetadata> ForkThreadAsync(
        string projectPath, Guid threadId, Guid? turnId, string title, bool includeTurn = true, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: true, async (_, ct) =>
        {
            await FlushTimelineAsync().ConfigureAwait(false);
            return await _store.ForkThreadAsync(projectPath, threadId, turnId, title, includeTurn, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task<(AgentThreadMetadata Thread, IReadOnlyList<AgentTimelineEvent> Timeline)> RewindTurnAsync(
        string projectPath, Guid threadId, Guid turnId, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: true, async (session, ct) =>
        {
            await CloseIdleSessionAsync(session, threadId, ct).ConfigureAwait(false);
            return await _store.RewindTurnAsync(projectPath, threadId, turnId, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task RecordTurnHistoryAsync(string projectPath, AgentTurnHistoryRecorded entry, CancellationToken cancellationToken = default)
        => _store.AppendAsync(projectPath, entry, cancellationToken);

    public Task<bool> DeleteThreadAsync(string projectPath, Guid threadId, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: false, async (session, ct) =>
        {
            await CloseIdleSessionAsync(session, threadId, ct).ConfigureAwait(false);
            bool deleted = await _store.DeleteAsync(projectPath, threadId, ct).ConfigureAwait(false);
            if (deleted)
            {
                lock (_gate)
                    _sessions.TryRemove(new KeyValuePair<Guid, Session>(threadId, session));
            }
            return deleted;
        }, cancellationToken, cancelActive: true);

    public Task CloseSessionAsync(string projectPath, Guid threadId, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: false,
            (session, ct) => CloseIdleSessionAsync(session, threadId, ct), cancellationToken, cancelActive: true);

    public Task<AgentThreadMetadata> SetPermissionModeAsync(
        string projectPath, Guid threadId, AgentPermissionMode mode, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: false, async (session, ct) =>
        {
            var thread = await _store.SetPermissionModeAsync(projectPath, threadId, mode, ct).ConfigureAwait(false);
            session.RememberedDecisions.Clear();
            await _bridge.SetPermissionModeAsync(threadId, mode, ct).ConfigureAwait(false);
            return thread;
        }, cancellationToken);
}
