using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.Agents;

public sealed record AgentTurnCallbacks(
    Action<AgentTimelineEvent> Timeline,
    Func<AgentPermissionRequest, CancellationToken, Task<AgentPermissionDecision>> Permission,
    Func<AgentUserQuestionRequest, CancellationToken, Task<IReadOnlyDictionary<string, string[]>>> Question);

public sealed class AgentRuntimeManager : IAsyncDisposable
{
    private readonly IAgentRuntimeBridge _bridge;
    private readonly AgentThreadStore _store;
    private readonly AgentBinaryLocator _locator;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<TimelineWork> _timeline = Channel.CreateUnbounded<TimelineWork>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _timelineReader;
    private readonly object _gate = new();
    private Task? _disposal;

    public AgentRuntimeManager(
        IAgentRuntimeBridge? bridge = null, AgentThreadStore? store = null, AgentBinaryLocator? locator = null, ILogger? logger = null)
    {
        _bridge = bridge ?? new WorkerAgentRuntimeBridge(logger: logger);
        _store = store ?? new AgentThreadStore();
        _locator = locator ?? new AgentBinaryLocator();
        _logger = logger ?? NullLogger.Instance;
        _bridge.TimelineEventReceived += EnqueueEvent;
        _bridge.Disconnected += OnDisconnected;
        _bridge.PermissionHandler = DecidePermissionAsync;
        _bridge.QuestionHandler = AnswerQuestionAsync;
        _timelineReader = ReadTimelineAsync();
    }

    public event Action<AgentTimelineEvent>? TimelineEventReceived;
    public event Action<AgentComposerSuggestionsChanged>? ComposerSuggestionsChanged;
    public event Action<Exception>? Disconnected;
    public Func<AgentPermissionRequest, CancellationToken, Task<AgentPermissionDecision>>? PermissionHandler { get; set; }
    public Func<AgentUserQuestionRequest, CancellationToken, Task<IReadOnlyDictionary<string, string[]>>>? QuestionHandler { get; set; }

    public Task<AgentWorkerHealth> StartAsync(CancellationToken cancellationToken = default) => _bridge.PingAsync(cancellationToken);

    public Task<AgentProviderProbeResult> ProbeProviderAsync(AgentProviderDescriptor provider, CancellationToken cancellationToken = default)
        => _locator.ProbeAsync(provider, cancellationToken);

    public async Task<IReadOnlyList<AgentModelDescriptor>> ListModelsAsync(
        string projectPath, AgentProviderDescriptor provider, Guid? threadId = null, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var probe = await ProbeProviderAsync(provider, linked.Token).ConfigureAwait(false);
            if (probe.ExecutablePath is { } executable)
            {
                var models = await _bridge.ListModelsAsync(new AgentModelListRequest(
                    provider.Id, provider.Kind, executable, GetWorkingDirectory(projectPath),
                    provider.Arguments, GetEnvironment(probe), threadId), linked.Token).ConfigureAwait(false);
                if (models.Count > 0)
                    return models;
            }
        }
        catch (Exception ex) when (!linked.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Could not discover models for agent provider {ProviderId}.", provider.Id);
        }
        linked.Token.ThrowIfCancellationRequested();
        return AgentModelCatalog.GetFallback(provider);
    }

    public async Task<IReadOnlyList<AgentComposerSuggestion>> ListComposerSuggestionsAsync(
        string projectPath, AgentProviderDescriptor provider, Guid? threadId = null, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var probe = await ProbeProviderAsync(provider, linked.Token).ConfigureAwait(false);
            if (probe.ExecutablePath is { } executable)
            {
                return await _bridge.ListComposerSuggestionsAsync(new AgentComposerSuggestionRequest(
                    provider.Kind, executable, GetWorkingDirectory(projectPath),
                    provider.Arguments, GetEnvironment(probe), threadId), linked.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (!linked.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Could not discover commands and skills for agent provider {ProviderId}.", provider.Id);
        }
        linked.Token.ThrowIfCancellationRequested();
        return [];
    }

    public Task<IReadOnlyList<AgentThreadMetadata>> ListThreadsAsync(string projectPath, CancellationToken cancellationToken = default)
        => _store.ListAsync(projectPath, cancellationToken);

    public Task<AgentThreadMetadata> CreateThreadAsync(
        string projectPath, AgentProviderDescriptor provider, string title, AgentPermissionMode mode = AgentPermissionMode.Auto,
        string? model = null, CancellationToken cancellationToken = default, string? reasoningEffort = null)
        => _store.CreateAsync(projectPath, provider.Id, title, mode, model, cancellationToken, reasoningEffort);

    public async Task<(AgentThreadMetadata Thread, AgentThreadModelChanged? Change)> SetModelAsync(
        string projectPath, Guid threadId, string? model, string? effort, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        await session.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await FlushTimelineAsync().ConfigureAwait(false);
            return await _store.SetModelAsync(projectPath, threadId, model, effort, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public Task<AgentThreadMetadata> RenameThreadAsync(
        string projectPath, Guid threadId, string title, CancellationToken cancellationToken = default)
        => _store.RenameAsync(projectPath, threadId, title, cancellationToken);

    public Task<IReadOnlyList<AgentTimelineEvent>> LoadTimelineAsync(
        string projectPath, Guid threadId, CancellationToken cancellationToken = default)
        => _store.ReadTimelineAsync(projectPath, threadId, cancellationToken);

    public async Task<AgentThreadMetadata> ForkThreadAsync(
        string projectPath, Guid threadId, Guid turnId, string title, bool includeTurn = true, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        if (!await session.Gate.WaitAsync(0, linked.Token).ConfigureAwait(false))
            throw new InvalidOperationException("This agent thread is already busy.");
        try
        {
            await FlushTimelineAsync().ConfigureAwait(false);
            return await _store.ForkThreadAsync(projectPath, threadId, turnId, title, includeTurn, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task<(AgentThreadMetadata Thread, IReadOnlyList<AgentTimelineEvent> Timeline)> RewindTurnAsync(
        string projectPath, Guid threadId, Guid turnId, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        if (!await session.Gate.WaitAsync(0, linked.Token).ConfigureAwait(false))
            throw new InvalidOperationException("This agent thread is already busy.");
        try
        {
            await _bridge.CloseSessionAsync(threadId, linked.Token).ConfigureAwait(false);
            await FlushTimelineAsync().ConfigureAwait(false);
            session.RememberedDecisions.Clear();
            return await _store.RewindTurnAsync(projectPath, threadId, turnId, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public Task RecordTurnHistoryAsync(string projectPath, AgentTurnHistoryRecorded entry, CancellationToken cancellationToken = default)
        => _store.AppendAsync(projectPath, entry, cancellationToken);

    public async Task<bool> DeleteThreadAsync(string projectPath, Guid threadId, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        await CancelTurnAsync(threadId, linked.Token).ConfigureAwait(false);
        await session.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await _bridge.CloseSessionAsync(threadId, linked.Token).ConfigureAwait(false);
            await FlushTimelineAsync().ConfigureAwait(false);
            return await _store.DeleteAsync(projectPath, threadId, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task CloseSessionAsync(string projectPath, Guid threadId, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        await CancelTurnAsync(threadId, linked.Token).ConfigureAwait(false);
        await session.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await _bridge.CloseSessionAsync(threadId, linked.Token).ConfigureAwait(false);
            await FlushTimelineAsync().ConfigureAwait(false);
            session.RememberedDecisions.Clear();
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task<AgentThreadMetadata> SetPermissionModeAsync(
        string projectPath, Guid threadId, AgentPermissionMode mode, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        await session.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var thread = await _store.SetPermissionModeAsync(projectPath, threadId, mode, linked.Token).ConfigureAwait(false);
            session.RememberedDecisions.Clear();
            await _bridge.SetPermissionModeAsync(threadId, mode, linked.Token).ConfigureAwait(false);
            return thread;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public Task<AgentTurnSummary> RunTurnAsync(
        string projectPath, Guid threadId, AgentProviderDescriptor provider, string text,
        IReadOnlyList<McpServerSpec> mcpServers, AgentContextSnapshot? context = null,
        CancellationToken cancellationToken = default)
        => RunTurnAsync(projectPath, provider, new AgentTurnRequest(threadId, Guid.NewGuid(), text, context),
            mcpServers, cancellationToken: cancellationToken);

    public async Task<AgentTurnSummary> RunTurnAsync(
        string projectPath, AgentProviderDescriptor provider, AgentTurnRequest request,
        IReadOnlyList<McpServerSpec> mcpServers, AgentTurnCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        Guid threadId = request.ThreadId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        if (!await session.Gate.WaitAsync(0, linked.Token).ConfigureAwait(false))
            throw new InvalidOperationException("This agent thread is already busy.");

        try
        {
            var thread = await _store.GetAsync(projectPath, threadId, linked.Token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Agent thread {threadId} does not exist.");
            if (provider.Id != thread.ProviderId)
                throw new ArgumentException("The provider must match the agent thread.", nameof(provider));
            var turn = new ActiveTurn(request.TurnId, thread.PermissionMode, linked, callbacks, thread.Model);
            session.Active = turn;
            try
            {
                if (thread.RestorePreviousMessages)
                    request = request with { PreviousMessages = await ReadPreviousMessagesAsync(projectPath, threadId, linked.Token).ConfigureAwait(false) };
                var probe = await ProbeProviderAsync(provider, linked.Token).ConfigureAwait(false);
                if (probe.ExecutablePath is not { } executable)
                    throw new FileNotFoundException(probe.Error);

                await _bridge.OpenSessionAsync(new AgentSessionOpenRequest(
                    threadId, provider.Kind, executable, GetWorkingDirectory(session.ProjectPath), thread.PermissionMode,
                    mcpServers, provider.Arguments, GetEnvironment(probe), provider.PermissionModes, thread.Model,
                    thread.ExternalSessionId, thread.ReasoningEffort),
                    linked.Token).ConfigureAwait(false);
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
        }
        finally
        {
            var finished = session.Active;
            session.Active = null;
            session.Gate.Release();
            finished?.Completed.TrySetResult();
        }
    }

    private async Task<IReadOnlyList<AgentConversationMessage>?> ReadPreviousMessagesAsync(
        string projectPath, Guid threadId, CancellationToken cancellationToken)
    {
        const int messageLimit = 32;
        const int characterLimit = 24_000;
        var timeline = await _store.ReadTimelineAsync(projectPath, threadId, cancellationToken).ConfigureAwait(false);
        var messages = new List<AgentConversationMessage>();
        int remaining = characterLimit;
        for (int i = timeline.Count - 1; i >= 0 && messages.Count < messageLimit && remaining > 0; i--)
        {
            var (role, text) = timeline[i] switch
            {
                AgentUserMessage user => ("user", user.Text),
                AgentAssistantMessageCompleted { ParentToolCallId: null } assistant => ("assistant", assistant.Text),
                _ => ("", "")
            };
            if (string.IsNullOrWhiteSpace(text))
                continue;

            string excerpt = text.Length <= remaining ? text : text[..remaining];
            messages.Add(new AgentConversationMessage(role, excerpt));
            remaining -= excerpt.Length;
        }
        messages.Reverse();
        return messages.Count == 0 ? null : messages;
    }

    private static string GetWorkingDirectory(string projectPath)
    {
        string path = Path.GetFullPath(projectPath);
        return Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
    }

    private static IReadOnlyDictionary<string, string> GetEnvironment(AgentProviderProbeResult probe)
    {
        var environment = new Dictionary<string, string>(
            probe.Provider.Environment ?? new Dictionary<string, string>(),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (probe.SearchPath is { } path)
            environment.TryAdd("PATH", path);
        return environment;
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

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turn.Token);
        if ((turn.Callbacks?.Permission ?? PermissionHandler) is not { } handler)
            return AgentPermissionDecision.RejectOnce;
        await FlushTimelineAsync().ConfigureAwait(false);
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

    private Session GetSession(string projectPath, Guid threadId)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
            var session = _sessions.GetOrAdd(threadId, _ => new Session(path));
            if (!string.Equals(session.ProjectPath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("The agent thread belongs to a different project.", nameof(projectPath));
            return session;
        }
    }

    private void EnqueueEvent(AgentTimelineEvent entry) => _timeline.Writer.TryWrite(new(entry, null, Stopwatch.GetTimestamp()));

    private Task FlushTimelineAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _timeline.Writer.TryWrite(new(null, completion)) ? completion.Task : _timelineReader;
    }

    private async Task ReadTimelineAsync()
    {
        await foreach (var item in _timeline.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Entry is AgentComposerSuggestionsChanged suggestions)
            {
                try
                {
                    ComposerSuggestionsChanged?.Invoke(suggestions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An agent composer suggestions consumer failed.");
                }
                continue;
            }
            if (item.Entry is { } entry && _sessions.TryGetValue(entry.ThreadId, out var session))
            {
                var turn = session.Active;
                if (turn is not null && turn.Id == entry.TurnId)
                {
                    switch (entry)
                    {
                        case AgentTurnStarted:
                            turn.Started.TrySetResult();
                            break;
                        case AgentTurnUsageUpdated usage:
                            turn.InputTokens = usage.InputTokens ?? turn.InputTokens;
                            turn.OutputTokens = usage.OutputTokens ?? turn.OutputTokens;
                            turn.CachedInputTokens = usage.CachedInputTokens ?? turn.CachedInputTokens;
                            turn.Model = usage.Model ?? turn.Model;
                            break;
                        case AgentTurnCompleted completed:
                            turn.TerminalEventReceived = true;
                            entry = completed with
                            {
                                DurationMs = completed.DurationMs
                                    ?? (long)Stopwatch.GetElapsedTime(turn.StartTimestamp, item.ReceivedTimestamp).TotalMilliseconds,
                                InputTokens = completed.InputTokens ?? turn.InputTokens,
                                OutputTokens = completed.OutputTokens ?? turn.OutputTokens,
                                CachedInputTokens = completed.CachedInputTokens ?? turn.CachedInputTokens,
                                Model = completed.Model ?? turn.Model
                            };
                            break;
                        case AgentTurnFailed or AgentTurnCancelled:
                            turn.TerminalEventReceived = true;
                            break;
                    }
                }
                try
                {
                    await _store.AppendAsync(session.ProjectPath, entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (turn != null)
                        turn.PersistenceError ??= ex;
                    _logger.LogError(ex, "Could not persist agent timeline for {ThreadId}.", entry.ThreadId);
                }
                try
                {
                    if (turn is not null && (entry.TurnId is null || turn.Id == entry.TurnId))
                        turn.Callbacks?.Timeline(entry);
                    TimelineEventReceived?.Invoke(entry);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An agent timeline consumer failed.");
                }
            }
            item.Completion?.TrySetResult();
        }
    }

    private void OnDisconnected(Exception error) => Disconnected?.Invoke(error);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            return new ValueTask(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        foreach (var session in _sessions.Values)
        {
            await session.Gate.WaitAsync().ConfigureAwait(false);
            session.Gate.Release();
        }

        try
        {
            await _bridge.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _bridge.TimelineEventReceived -= EnqueueEvent;
            _bridge.Disconnected -= OnDisconnected;
            _bridge.PermissionHandler = null;
            _bridge.QuestionHandler = null;
            _timeline.Writer.TryComplete();
            await _timelineReader.ConfigureAwait(false);
        }
    }

    private sealed class Session(string projectPath)
    {
        public string ProjectPath { get; } = projectPath;
        public SemaphoreSlim Gate { get; } = new(1);
        public ConcurrentDictionary<(string? Server, string Tool, string? Description), AgentPermissionDecision> RememberedDecisions { get; } = new();
        public volatile ActiveTurn? Active;
    }

    private sealed class ActiveTurn(Guid id, AgentPermissionMode mode,
        CancellationTokenSource cancellation, AgentTurnCallbacks? callbacks, string? model)
    {
        public Guid Id { get; } = id;
        public AgentPermissionMode Mode { get; } = mode;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token { get; } = cancellation.Token;
        public AgentTurnCallbacks? Callbacks { get; } = callbacks;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long StartTimestamp { get; } = Stopwatch.GetTimestamp();
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? CachedInputTokens { get; set; }
        public string? Model { get; set; } = model;
        public bool TerminalEventReceived { get; set; }
        public Exception? PersistenceError { get; set; }
    }

    private sealed record TimelineWork(AgentTimelineEvent? Entry, TaskCompletionSource? Completion, long ReceivedTimestamp = 0);
}
