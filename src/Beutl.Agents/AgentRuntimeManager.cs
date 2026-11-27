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

public sealed partial class AgentRuntimeManager : IAsyncDisposable
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

    public async Task<bool?> CheckProviderAuthenticationAsync(AgentProviderDescriptor provider, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeProviderAsync(provider, cancellationToken).ConfigureAwait(false);
        return await _locator.CheckAuthenticationAsync(probe, cancellationToken).ConfigureAwait(false);
    }

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

    public static string GetWorkingDirectory(string projectPath)
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

    private Session GetSession(string projectPath, Guid threadId)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal != null, this);
            var session = _sessions.GetOrAdd(threadId, _ => new Session(path));
            if (!string.Equals(session.ProjectPath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("The agent thread belongs to a different project.", nameof(projectPath));
            return session;
        }
    }

    private async Task<T> RunWithSessionGateAsync<T>(
        string projectPath, Guid threadId, bool failIfBusy,
        Func<Session, CancellationTokenSource, Task<T>> action,
        CancellationToken cancellationToken, bool cancelActive = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var session = GetSession(projectPath, threadId);
        if (cancelActive)
            await CancelTurnAsync(threadId, linked.Token).ConfigureAwait(false);
        if (failIfBusy)
        {
            if (!await session.Gate.WaitAsync(0, linked.Token).ConfigureAwait(false))
                throw new InvalidOperationException("This agent thread is already busy.");
        }
        else
        {
            await session.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }

        try
        {
            return await action(session, linked).ConfigureAwait(false);
        }
        finally
        {
            var finished = session.Active;
            session.Active = null;
            session.Gate.Release();
            finished?.Completed.TrySetResult();
        }
    }

    private Task<T> RunWithSessionGateAsync<T>(
        string projectPath, Guid threadId, bool failIfBusy,
        Func<Session, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken, bool cancelActive = false)
        => RunWithSessionGateAsync(projectPath, threadId, failIfBusy,
            (session, linked) => action(session, linked.Token), cancellationToken, cancelActive);

    private Task RunWithSessionGateAsync(
        string projectPath, Guid threadId, bool failIfBusy,
        Func<Session, CancellationToken, Task> action,
        CancellationToken cancellationToken, bool cancelActive = false)
        => RunWithSessionGateAsync<bool>(projectPath, threadId, failIfBusy, async (session, linked) =>
        {
            await action(session, linked.Token).ConfigureAwait(false);
            return true;
        }, cancellationToken, cancelActive);

    private async Task<AgentThreadMetadata> GetThreadForProviderAsync(
        string projectPath, Guid threadId, AgentProviderDescriptor provider, CancellationToken cancellationToken)
    {
        var thread = await _store.GetAsync(projectPath, threadId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Agent thread {threadId} does not exist.");
        if (thread.ProviderId != provider.Id)
            throw new ArgumentException("The provider must match the agent thread.", nameof(provider));
        return thread;
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
            _lifetime.Dispose();
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
        public AgentContextUsage? ContextUsage { get; set; }
        public ConcurrentDictionary<string, byte> PresentedPermissions { get; } = new(StringComparer.Ordinal);
        public string? Model { get; set; } = model;
        public bool TerminalEventReceived { get; set; }
        public Exception? PersistenceError { get; set; }
    }

    private sealed record TimelineWork(AgentTimelineEvent? Entry, TaskCompletionSource? Completion, long ReceivedTimestamp = 0);
}
