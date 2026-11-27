using Beutl.Language;

namespace Beutl.Agents;

public sealed partial class AgentRuntimeManager
{
    public Task<AgentThreadMetadata> CompactContextAsync(
        string projectPath, Guid threadId, AgentProviderDescriptor provider, IReadOnlyList<McpServerSpec> mcpServers,
        AgentTurnCallbacks? callbacks = null, CancellationToken cancellationToken = default)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy: true, async (session, linked) =>
        {
            await FlushTimelineAsync().ConfigureAwait(false);
            var thread = await GetThreadForProviderAsync(projectPath, threadId, provider, linked.Token).ConfigureAwait(false);

            var turn = new ActiveTurn(Guid.NewGuid(), thread.PermissionMode, linked, callbacks, thread.Model);
            session.Active = turn;
            var started = new AgentCompactionStarted(threadId, turn.Id, DateTimeOffset.UtcNow);
            await _store.AppendAsync(projectPath, started, linked.Token).ConfigureAwait(false);
            PublishTimeline(started, turn);
            try
            {
                var result = new AgentCompactionResult(AgentCompactionStatus.Unsupported);
                if (thread.ExternalSessionId is not null && !thread.RestorePreviousMessages && provider.Kind != AgentProviderKind.Acp)
                {
                    var probe = await ProbeProviderAsync(provider, linked.Token).ConfigureAwait(false);
                    if (probe.ExecutablePath is { } executable)
                    {
                        await OpenSessionAsync(thread, probe, executable, session.ProjectPath, mcpServers, linked.Token).ConfigureAwait(false);
                        result = await _bridge.CompactContextAsync(threadId, turn.Id, linked.Token).ConfigureAwait(false);
                        await FlushTimelineAsync().ConfigureAwait(false);
                    }
                }

                linked.Token.ThrowIfCancellationRequested();
                if (turn.PersistenceError is { } error)
                    throw new IOException("Could not save the agent timeline.", error);
                if (result.Status is AgentCompactionStatus.Failed or AgentCompactionStatus.Cancelled)
                    throw new InvalidOperationException(result.Error ?? AgentStrings.CompactionFailed);

                bool useHandoff = result.Status == AgentCompactionStatus.Unsupported;
                var completed = new AgentCompactionCompleted(threadId, turn.Id, DateTimeOffset.UtcNow, useHandoff,
                    ContextUsage: turn.ContextUsage);
                if (useHandoff)
                {
                    await CloseIdleSessionAsync(session, threadId, linked.Token).ConfigureAwait(false);
                    thread = await _store.ResetContextAsync(projectPath, completed, true, linked.Token).ConfigureAwait(false);
                }
                else
                {
                    await _store.AppendAsync(projectPath, completed, linked.Token).ConfigureAwait(false);
                }
                PublishTimeline(completed, turn);
                return thread;
            }
            catch
            {
                await FlushTimelineAsync().ConfigureAwait(false);
                EnqueueEvent(new AgentCompactionCompleted(threadId, turn.Id, DateTimeOffset.UtcNow, Succeeded: false));
                await FlushTimelineAsync().ConfigureAwait(false);
                throw;
            }
        }, cancellationToken);

    public Task<AgentTurnSummary> RunTurnAsync(
        string projectPath, Guid threadId, AgentProviderDescriptor provider, string text,
        IReadOnlyList<McpServerSpec> mcpServers, AgentContextSnapshot? context = null,
        CancellationToken cancellationToken = default)
        => RunTurnAsync(projectPath, provider, new AgentTurnRequest(threadId, Guid.NewGuid(), text, context),
            mcpServers, cancellationToken: cancellationToken);

    public Task<AgentTurnSummary> RunTurnAsync(
        string projectPath, AgentProviderDescriptor provider, AgentTurnRequest request,
        IReadOnlyList<McpServerSpec> mcpServers, AgentTurnCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        Guid threadId = request.ThreadId;
        return RunWithSessionGateAsync(projectPath, threadId, failIfBusy: true, async (session, linked) =>
        {
            var thread = await GetThreadForProviderAsync(projectPath, threadId, provider, linked.Token).ConfigureAwait(false);
            var turn = new ActiveTurn(request.TurnId, thread.PermissionMode, linked, callbacks, thread.Model);
            session.Active = turn;
            try
            {
                var probe = await ProbeProviderAsync(provider, linked.Token).ConfigureAwait(false);
                if (probe.ExecutablePath is not { } executable)
                    throw new FileNotFoundException(probe.Error);

                var sessionInfo = await OpenSessionAsync(thread, probe, executable, session.ProjectPath, mcpServers, linked.Token).ConfigureAwait(false);
                if (thread.RestorePreviousMessages || (sessionInfo.IsNewSession && sessionInfo.ExternalSessionId is null))
                    request = request with { PreviousMessages = await ReadPreviousMessagesAsync(projectPath, threadId, linked.Token).ConfigureAwait(false) };
                var result = await _bridge.RunTurnAsync(request, linked.Token)
                    .ConfigureAwait(false);
                await FlushTimelineAsync().ConfigureAwait(false);
                if (turn.PersistenceError is { } error)
                    throw new IOException("Could not save the agent timeline.", error);
                return result;
            }
            catch (Exception ex)
            {
                await FlushTimelineAsync().ConfigureAwait(false);
                if (!turn.TerminalEventReceived)
                {
                    var now = DateTimeOffset.UtcNow;
                    EnqueueEvent(linked.IsCancellationRequested
                        ? new AgentTurnCancelled(threadId, turn.Id, now)
                        : new AgentTurnFailed(threadId, turn.Id, now, ex.Message));
                    await FlushTimelineAsync().ConfigureAwait(false);
                }
                throw;
            }
        }, cancellationToken);
    }

    private Task<AgentSessionInfo> OpenSessionAsync(AgentThreadMetadata thread, AgentProviderProbeResult probe, string executable,
        string projectPath, IReadOnlyList<McpServerSpec> mcpServers, CancellationToken cancellationToken)
        => _bridge.OpenSessionAsync(new AgentSessionOpenRequest(
            thread.Id, probe.Provider.Kind, executable, GetWorkingDirectory(projectPath), thread.PermissionMode,
            mcpServers, probe.Provider.Arguments, GetEnvironment(probe), probe.Provider.PermissionModes, thread.Model,
            thread.ExternalSessionId, thread.ReasoningEffort), cancellationToken);

    private async Task<IReadOnlyList<AgentConversationMessage>?> ReadPreviousMessagesAsync(
        string projectPath, Guid threadId, CancellationToken cancellationToken)
    {
        const int messageLimit = 10;
        const int messageCharacterLimit = 600;
        var timeline = await _store.ReadTimelineAsync(projectPath, threadId, cancellationToken).ConfigureAwait(false);
        var messages = new List<AgentConversationMessage>();
        AgentUserMessage? firstUser = null;
        for (int i = timeline.Count - 1; i >= 0 && timeline[i] is not AgentContextCleared; i--)
        {
            if (timeline[i] is AgentUserMessage userMessage && !string.IsNullOrWhiteSpace(userMessage.Text))
                firstUser = userMessage;
            if (messages.Count >= messageLimit)
                continue;
            var (role, text) = timeline[i] switch
            {
                AgentUserMessage user => ("user", user.Text),
                AgentAssistantMessageCompleted { ParentToolCallId: null } assistant => ("assistant", assistant.Text),
                _ => ("", "")
            };
            if (string.IsNullOrWhiteSpace(text))
                continue;

            messages.Add(new AgentConversationMessage(role, AgentThreadStore.TruncateOneLine(text, messageCharacterLimit)));
        }
        messages.Reverse();
        if (firstUser is not null)
        {
            var firstMessage = new AgentConversationMessage("user", AgentThreadStore.TruncateOneLine(firstUser.Text, messageCharacterLimit));
            if (!messages.Contains(firstMessage))
                messages.Insert(0, firstMessage);
        }
        return messages.Count == 0 ? null : messages;
    }

    public async Task CancelTurnAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(threadId, out var session) || session.Active is not { } turn)
            return;
        try
        {
            await turn.Cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The turn completed before it could be cancelled.
        }
        await turn.Completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SteerTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(request.ThreadId, out var session)
            || session.Active is not { } turn || turn.Id != request.TurnId || turn.Token.IsCancellationRequested)
            return false;

        var ready = await Task.WhenAny(turn.Started.Task, turn.Completed.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (ready != turn.Started.Task || session.Active != turn || turn.Token.IsCancellationRequested)
            return false;

        bool steered = await _bridge.SteerTurnAsync(request, cancellationToken).ConfigureAwait(false);
        if (steered)
            await FlushTimelineAsync().ConfigureAwait(false);
        return steered;
    }

    public Task<AgentBackgroundStopResult> StopBackgroundTaskAsync(
        Guid threadId, Guid turnId, string toolCallId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(threadId, out var session) || session.Active is not { } turn
            || turn.Id != turnId || turn.Token.IsCancellationRequested)
            return Task.FromResult(AgentBackgroundStopResult.Unavailable);
        return _bridge.StopBackgroundTaskAsync(threadId, turnId, toolCallId, cancellationToken);
    }

    private async Task<AgentPermissionDecision> DecidePermissionAsync(AgentPermissionRequest request, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(request.ThreadId, out var session) || session.Active is not { } turn || turn.Id != request.TurnId)
            return AgentPermissionDecision.RejectOnce;

        if (turn.Mode == AgentPermissionMode.FullAccess)
            return AgentPermissionDecision.AllowOnce;

        var key = (request.McpServerName, request.ToolName, request.Description);
        if (session.RememberedDecisions.TryGetValue(key, out var remembered))
            return remembered;

        if ((turn.Callbacks?.Permission ?? PermissionHandler) is not { } handler)
            return AgentPermissionDecision.RejectOnce;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turn.Token);
        await FlushTimelineAsync().ConfigureAwait(false);
        if (turn.Callbacks is not null)
            turn.PresentedPermissions.TryAdd(request.RequestId, 0);
        var decision = await handler(request, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
        if (decision is AgentPermissionDecision.AllowAlways or AgentPermissionDecision.RejectAlways)
            session.RememberedDecisions[key] = decision;
        return decision;
    }

    private async Task<IReadOnlyDictionary<string, string[]>> AnswerQuestionAsync(
        AgentUserQuestionRequest request, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(request.ThreadId, out var session) || session.Active is not { } turn || turn.Id != request.TurnId
            || (turn.Callbacks?.Question ?? QuestionHandler) is not { } handler)
            return new Dictionary<string, string[]>();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turn.Token);
        await FlushTimelineAsync().ConfigureAwait(false);
        return await handler(request, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
    }
}
