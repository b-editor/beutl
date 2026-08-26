using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed class AgentHostEndpoint : IAsyncDisposable
{
    internal const int DefaultPort = 59737;

    private static readonly ILogger s_logger = Log.CreateLogger<AgentHostEndpoint>();
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly AiAgentConfig _config;
    private readonly int _preferredPort;
    private readonly Func<CancellationToken, Task> _beforeStopAsync;
    private readonly Func<CancellationToken, Task> _afterStartAsync;
    private readonly Func<RenderJobManager> _renderJobManagerFactory;
    private readonly Func<Task>? _admissionProbe;
    private readonly AdmissionGate _admissionGate = new();
    private readonly object _lifecycleLock = new();
    private bool _stopRequested;
    private WebApplication? _application;
    private Task? _startTask;
    private Task? _drainTask;
    private Task<AgentHostShutdownScope>? _prepareTask;

    public AgentHostEndpoint(ProjectService projectService, EditorService editorService)
        : this(projectService, editorService, GlobalConfiguration.Instance.AiAgentConfig)
    {
    }

    internal AgentHostEndpoint(ProjectService projectService, EditorService editorService, AiAgentConfig config)
        : this(
            projectService,
            editorService,
            DefaultPort,
            ResolveToken(config),
            config,
            beforeStopAsync: null,
            afterStartAsync: null,
            renderJobManagerFactory: null,
            admissionProbe: null)
    {
    }

    // A fresh 128-bit local secret; a shared constant would let any local process that knows it drive
    // the loopback editing endpoint.
    internal static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    internal static string ResolveToken(AiAgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(config.LiveMcpToken))
        {
            return config.LiveMcpToken;
        }

        string token = GenerateToken();
        config.LiveMcpToken = token;
        return token;
    }

    // Prefer the workspace the user chose on the AI Agents settings page (read at start, so a
    // restart picks up a change) over the shared host-computed default.
    internal static string ResolveWorkspaceRoot(AiAgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        string configured = config.WorkspaceRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string? env = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents)
            ? Directory.GetCurrentDirectory()
            : documents;
    }

    internal AgentHostEndpoint(ProjectService projectService, EditorService editorService, int preferredPort, string token)
        : this(
            projectService,
            editorService,
            preferredPort,
            token,
            GlobalConfiguration.Instance.AiAgentConfig,
            static _ => Task.CompletedTask,
            admissionProbe: null)
    {
    }

    internal AgentHostEndpoint(
        ProjectService projectService,
        EditorService editorService,
        int preferredPort,
        string token,
        Func<CancellationToken, Task> beforeStopAsync,
        Func<CancellationToken, Task>? afterStartAsync = null,
        Func<RenderJobManager>? renderJobManagerFactory = null,
        Func<Task>? admissionProbe = null)
        : this(
            projectService,
            editorService,
            preferredPort,
            token,
            GlobalConfiguration.Instance.AiAgentConfig,
            beforeStopAsync,
            afterStartAsync,
            renderJobManagerFactory,
            admissionProbe)
    {
    }

    private AgentHostEndpoint(
        ProjectService projectService,
        EditorService editorService,
        int preferredPort,
        string token,
        AiAgentConfig config,
        Func<CancellationToken, Task>? beforeStopAsync = null,
        Func<CancellationToken, Task>? afterStartAsync = null,
        Func<RenderJobManager>? renderJobManagerFactory = null,
        Func<Task>? admissionProbe = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (preferredPort is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredPort));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token must not be empty.", nameof(token));
        }

        _projectService = projectService;
        _editorService = editorService;
        _config = config;
        _preferredPort = preferredPort;
        _beforeStopAsync = beforeStopAsync ?? (static _ => Task.CompletedTask);
        _afterStartAsync = afterStartAsync ?? (static _ => Task.CompletedTask);
        _renderJobManagerFactory = renderJobManagerFactory ?? (static () => new RenderJobManager());
        _admissionProbe = admissionProbe;
        Token = token;
    }

    public string Token { get; }

    public Uri? EndpointUri { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock)
            {
                return !_stopRequested && _application is not null;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (_lifecycleLock)
        {
            // A stop requested before (or during) startup must win: never start after RequestStop.
            if (_stopRequested)
            {
                return Task.CompletedTask;
            }

            // Retain the single startup task so a concurrent stop can join startup before reporting
            // that the endpoint is fully drained.
            if (_startTask is not null)
            {
                return _startTask;
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startTask = completion.Task;
        }

        _ = CompleteStartAsync(completion, cancellationToken);
        return completion.Task;
    }

    private async Task CompleteStartAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            await StartCoreAsync(cancellationToken).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            ResetFailedStartAttempt(completion.Task);
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            ResetFailedStartAttempt(completion.Task);
            completion.TrySetException(ex);
        }
    }

    private void ResetFailedStartAttempt(Task startTask)
    {
        lock (_lifecycleLock)
        {
            // Preserve the task when a concurrent stop already captured it for draining. Otherwise
            // a transient cancellation or startup failure must not make this endpoint permanently
            // unable to retry.
            if (!_stopRequested && ReferenceEquals(_startTask, startTask))
            {
                _startTask = null;
            }
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            // RequestStop may win after StartAsync publishes its retained task but before this
            // runner begins. In that case there is no host to create or drain.
            if (_stopRequested)
            {
                return;
            }
        }

        int port = _preferredPort;
        while (true)
        {
            WebApplication app = CreateApplication(port);

            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                await _afterStartAsync(cancellationToken).ConfigureAwait(false);

                string address = app.Services
                    .GetRequiredService<IServer>()
                    .Features
                    .Get<IServerAddressesFeature>()!
                    .Addresses
                    .Single();

                var endpointUri = new Uri(new Uri(address), "/mcp");

                bool stopRequested;
                lock (_lifecycleLock)
                {
                    stopRequested = _stopRequested;
                    // The canonical drain takes ownership after the retained startup task
                    // completes. Keep the app reachable even when stop won during startup.
                    _application = app;
                    if (!stopRequested)
                    {
                        // Publish EndpointUri only after the stop check: RequestStopCore already
                        // cleared it (while still null), so setting it before this check would leave
                        // a dead URL visible to the settings page after a stop-during-startup race.
                        EndpointUri = endpointUri;
                    }
                }

                return;
            }
            catch (Exception ex) when (IsAddressInUse(ex))
            {
                await app.DisposeAsync().ConfigureAwait(false);

                lock (_lifecycleLock)
                {
                    if (_stopRequested)
                    {
                        return;
                    }
                }

                if (port >= IPEndPoint.MaxPort)
                {
                    throw;
                }

                port++;
            }
            catch
            {
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    // Fire-and-forget entry point for the app shell: StartAsync failures would otherwise be
    // unobserved on a discarded task, leaving the live MCP endpoint silently down.
    public void StartInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "The agent host endpoint failed to start; the live MCP endpoint is unavailable.");
            }
        });
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task drain = RequestStopCore();
        return cancellationToken.CanBeCanceled
            ? drain.WaitAsync(cancellationToken)
            : drain;
    }

    public void RequestStop()
    {
        _ = ObserveDrainFailureAsync(RequestStopCore());
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(RequestStopCore());
    }

    /// <summary>
    /// Reversibly pauses authenticated MCP ingress and drains detached render jobs. Disposing the
    /// returned scope resumes ingress unless <see cref="AgentHostShutdownScope.Commit"/> was called.
    /// </summary>
    internal Task<AgentHostShutdownScope> BeginShutdownDrainAsync(
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<AgentHostShutdownScope> completion;
        Task? startTask;
        lock (_lifecycleLock)
        {
            if (_prepareTask is not null)
            {
                throw new InvalidOperationException("Agent host shutdown preparation is already in progress.");
            }

            if (_stopRequested)
            {
                return Task.FromResult(new AgentHostShutdownScope(this));
            }

            startTask = _startTask;
            completion = new TaskCompletionSource<AgentHostShutdownScope>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _prepareTask = completion.Task;
        }

        _ = CompletePrepareAsync(completion, startTask, cancellationToken);
        return completion.Task;
    }

    private async Task CompletePrepareAsync(
        TaskCompletionSource<AgentHostShutdownScope> completion,
        Task? startTask,
        CancellationToken cancellationToken)
    {
        try
        {
            completion.TrySetResult(await PrepareCoreAsync(startTask, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_prepareTask, completion.Task))
                {
                    _prepareTask = null;
                }
            }

            if (ex is OperationCanceledException canceled)
            {
                completion.TrySetCanceled(canceled.CancellationToken);
            }
            else
            {
                completion.TrySetException(ex);
            }
        }
    }

    private async Task<AgentHostShutdownScope> PrepareCoreAsync(
        Task? startTask,
        CancellationToken cancellationToken)
    {
        try
        {
            if (startTask is not null)
            {
                try
                {
                    await startTask.ConfigureAwait(false);
                }
                catch when (_stopRequested)
                {
                }
            }

            await _admissionGate.PauseAndDrainAsync(cancellationToken).ConfigureAwait(false);

            WebApplication? app;
            lock (_lifecycleLock)
            {
                app = _application;
            }

            if (app is not null)
            {
                await app.Services
                    .GetRequiredService<RenderJobManager>()
                    .CancelAndDrainAsync()
                    .ConfigureAwait(false);
            }

            var scope = new AgentHostShutdownScope(this);
            lock (_lifecycleLock)
            {
                if (_stopRequested)
                {
                    scope.Commit();
                }
            }

            return scope;
        }
        catch
        {
            lock (_lifecycleLock)
            {
                if (!_stopRequested)
                {
                    _admissionGate.Resume();
                    _prepareTask = null;
                }
            }

            throw;
        }
    }

    private void ReleaseShutdownScope(AgentHostShutdownScope scope, bool committed)
    {
        lock (_lifecycleLock)
        {
            _prepareTask = null;

            if (!committed && !_stopRequested)
            {
                _admissionGate.Resume();
            }
        }
    }

    private WebApplication CreateApplication(int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(AgentHostEndpoint).Assembly.FullName
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        string workspaceRoot = ResolveWorkspaceRoot(_config);

        builder.Services
            .AddSingleton(_projectService)
            .AddSingleton(_editorService)
            .AddSingleton<LiveSessionSource>()
            .AddSingleton<IProjectSessionGateway, EditorProjectSessionGateway>()
            .AddSingleton(_ => new CreativeMemoryStore(workspaceRoot))
            .AddSingleton<AgentSessionManager>()
            .AddSingleton<IWorkspaceGuard>(_ => new WorkspaceGuard(workspaceRoot))
            .AddSingleton<IOutputOperationLeaseProvider>(_ => _editorService)
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<StillRenderer>()
            .AddSingleton<StoryboardRenderer>()
            .AddSingleton<MotionVariationAnalyzer>()
            .AddSingleton<AudioRhythmAnalyzer>()
            .AddSingleton<QualityAnalyzer>()
            .AddSingleton<EncoderRegistration>()
            .AddSingleton<VideoExporter>()
            .AddSingleton(_ => _renderJobManagerFactory());

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithRequestFilters(filters => filters.AddToolkitCallToolErrorFilter())
            .WithTools<AgentHostTools>()
            .WithTools<SessionTools>()
            .WithTools<QueryTools>()
            .WithTools<DesignTools>()
            .WithTools<EditTools>()
            .WithTools<RenderTools>();

        WebApplication app = builder.Build();
        // Give the background-job manager the same explicit lifetime as this host. Resolving it
        // here also prevents shutdown from constructing a never-used manager only to dispose it.
        _ = app.Services.GetRequiredService<RenderJobManager>();
        app.Use(RequireToken);
        app.Use(AdmissionMiddleware);
        app.MapMcp("/mcp");
        return app;
    }

    // Latch ingress and publish one canonical, failure-bearing drain task in the same critical
    // section StartAsync uses. Caller cancellation only limits StopAsync's wait; it never abandons
    // the retained cleanup that every later stop/dispose call joins.
    private Task RequestStopCore()
    {
        TaskCompletionSource completion;
        Task? startTask;
        Task<AgentHostShutdownScope>? prepareTask;
        lock (_lifecycleLock)
        {
            _stopRequested = true;
            EndpointUri = null;
            _admissionGate.Pause();

            if (_drainTask is not null)
            {
                return _drainTask;
            }

            startTask = _startTask;
            prepareTask = _prepareTask;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _drainTask = completion.Task;
        }

        _ = CompleteDrainAsync(completion, startTask, prepareTask);
        return completion.Task;
    }

    private async Task CompleteDrainAsync(
        TaskCompletionSource completion,
        Task? startTask,
        Task<AgentHostShutdownScope>? prepareTask)
    {
        try
        {
            await DrainCoreAsync(startTask, prepareTask).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DrainCoreAsync(
        Task? startTask,
        Task<AgentHostShutdownScope>? prepareTask)
    {
        if (startTask is not null)
        {
            try
            {
                await startTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // StartAsync owns reporting startup failures. Its cleanup completes before the
                // startup task faults, so observing it here is sufficient for lifecycle quiescence.
                _ = ex;
            }
        }

        if (prepareTask is not null)
        {
            try
            {
                await prepareTask.ConfigureAwait(false);
            }
            catch when (_stopRequested)
            {
            }
        }

        await _admissionGate.PauseAndDrainAsync(CancellationToken.None).ConfigureAwait(false);

        WebApplication? app;
        lock (_lifecycleLock)
        {
            app = _application;
            _application = null;
        }

        if (app is null)
        {
            return;
        }

        await StopAndDisposeAsync(app, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task ObserveDrainFailureAsync(Task drain)
    {
        try
        {
            await drain.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "The agent host endpoint failed to drain.");
        }
    }

    private async Task StopAndDisposeAsync(WebApplication app, CancellationToken cancellationToken)
    {
        try
        {
            await _beforeStopAsync(cancellationToken).ConfigureAwait(false);
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // Background jobs outlive their initiating MCP request. Cancel and await every
                // terminal path before the host releases the project/editor services they lease.
                await app.Services
                    .GetRequiredService<RenderJobManager>()
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    internal sealed class AgentHostShutdownScope : IDisposable, IAsyncDisposable
    {
        private readonly AgentHostEndpoint _owner;
        private int _released;
        private bool _committed;

        internal AgentHostShutdownScope(AgentHostEndpoint owner)
        {
            _owner = owner;
        }

        internal void Commit() => _committed = true;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.ReleaseShutdownScope(this, _committed);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AdmissionGate
    {
        private readonly object _sync = new();
        private bool _paused;
        private int _active;
        private TaskCompletionSource? _drained;

        public Lease? TryEnter()
        {
            lock (_sync)
            {
                if (_paused)
                {
                    return null;
                }

                _active++;
                var lease = new Lease(this);
                return lease;
            }
        }

        public void Pause()
        {
            lock (_sync)
            {
                _paused = true;
                if (_active == 0)
                {
                    _drained?.TrySetResult();
                    _drained = null;
                }
            }
        }

        public Task PauseAndDrainAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task drained;
            lock (_sync)
            {
                _paused = true;
                if (_active == 0)
                {
                    return Task.CompletedTask;
                }

                _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                drained = _drained.Task;
            }

            return cancellationToken.CanBeCanceled ? drained.WaitAsync(cancellationToken) : drained;
        }

        public void Resume()
        {
            lock (_sync)
            {
                _paused = false;
            }
        }

        private void Release(Lease lease)
        {
            lock (_sync)
            {
                if (lease.Released)
                {
                    return;
                }

                lease.Released = true;
                _active--;
                if (_active == 0)
                {
                    _drained?.TrySetResult();
                    _drained = null;
                }

            }
        }

        public sealed class Lease : IDisposable
        {
            private readonly AdmissionGate _owner;
            internal Lease(AdmissionGate owner) => _owner = owner;
            internal bool Released { get; set; }
            public void Dispose() => _owner.Release(this);
        }
    }

    private static bool IsAddressInUse(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AddressInUseException)
            {
                return true;
            }

            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
            {
                return true;
            }
        }

        return false;
    }

    // The token travels only in the standard Authorization header — never in
    // the URL, where it would leak into client configs, logs, and history.
    private async Task RequireToken(HttpContext context, RequestDelegate next)
    {
        const string scheme = "Bearer ";
        string? authorization = context.Request.Headers.Authorization;

        if (authorization is null
            || !authorization.StartsWith(scheme, StringComparison.Ordinal)
            || !FixedTimeTokenEquals(authorization[scheme.Length..], Token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private async Task AdmissionMiddleware(HttpContext context, RequestDelegate next)
    {
        AdmissionGate.Lease? lease = _admissionGate.TryEnter();
        if (lease is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "1";
            return;
        }

        try
        {
            if (_admissionProbe is { } probe)
            {
                await probe().ConfigureAwait(false);
            }
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    // Constant-time compare: the token drives the editing surface even on loopback.
    private static bool FixedTimeTokenEquals(string provided, string expected)
    {
        if (provided.Length != expected.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
