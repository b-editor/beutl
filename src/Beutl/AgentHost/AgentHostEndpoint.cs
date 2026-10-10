using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;
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

    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(2);
    private static readonly ILogger s_logger = Log.CreateLogger<AgentHostEndpoint>();
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly AiAgentConfig _config;
    private readonly int _preferredPort;
    private readonly Func<CancellationToken, Task>? _beforeStart;
    private readonly Func<string>? _tokenFactory;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly AgentHostInstanceRegistry _instanceRegistry;
    private AgentHostInstanceRouter? _instanceRouter;
    private readonly ExtensionMcpToolCatalog _extensionTools;
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _startupCancellation = new();
    private bool _stopRequested;
    private WebApplication? _application;
    private Uri? _endpointUri;
    private Uri? _connectionUri;
    private AgentHostEndpointFailover? _failover;
    private Task? _failoverTask;
    private Task? _startupTask;
    private Task? _stopTask;
    private IDisposable? _instanceRegistration;

    public AgentHostEndpoint(ProjectService projectService, EditorService editorService)
        : this(projectService, editorService, GlobalConfiguration.Instance.AiAgentConfig)
    {
    }

    internal AgentHostEndpoint(ProjectService projectService, EditorService editorService, AiAgentConfig config,
        string? tokenStoreDirectory = null)
        : this(projectService, editorService, DefaultPort, "", config,
            tokenFactory: () => ResolveToken(config, tokenStoreDirectory))
    {
    }

    internal static string ResolveToken(AiAgentConfig config, string? tokenStoreDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return LiveMcpTokenStore.GetOrCreate(
            tokenStoreDirectory ?? BeutlEnvironment.GetHomeDirectoryPath(), config.LiveMcpToken);
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

    internal AgentHostEndpoint(
        ProjectService projectService,
        EditorService editorService,
        int preferredPort,
        string token,
        Func<CancellationToken, Task>? beforeStart = null,
        string? registryDirectory = null)
        : this(
            projectService,
            editorService,
            preferredPort,
            token,
            GlobalConfiguration.Instance.AiAgentConfig,
            beforeStart,
            registryDirectory)
    {
    }

    private AgentHostEndpoint(
        ProjectService projectService,
        EditorService editorService,
        int preferredPort,
        string token,
        AiAgentConfig config,
        Func<CancellationToken, Task>? beforeStart = null,
        string? registryDirectory = null,
        Func<string>? tokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (preferredPort is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredPort));
        }

        if (string.IsNullOrWhiteSpace(token) && tokenFactory is null)
        {
            throw new ArgumentException("Token must not be empty.", nameof(token));
        }

        _projectService = projectService;
        _editorService = editorService;
        _config = config;
        _preferredPort = preferredPort;
        _beforeStart = beforeStart;
        _tokenFactory = tokenFactory;
        Token = token;
        _instanceRegistry = new AgentHostInstanceRegistry(registryDirectory
            ?? Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "agent-hosts"));
        if (tokenFactory is null)
            _instanceRouter = CreateInstanceRouter(token);
        _extensionTools = new ExtensionMcpToolCatalog(editorService);
    }

    public string Token { get; private set; }

    public string InstanceId => _instanceId;

    private AgentHostInstanceRouter InstanceRouter => _instanceRouter
        ?? throw new InvalidOperationException("The live MCP credentials have not been initialized.");

    private AgentHostInstanceRouter CreateInstanceRouter(string token)
        => new(_instanceRegistry, _projectService, _editorService, token,
            () => ResolveWorkspaceRoot(_config), _instanceId);

    /// <summary>
    /// The application's AI services for the AI tools. Set once the API clients exist; until then,
    /// and in hosts without them, the AI tools report that generation is unavailable.
    /// </summary>
    internal IAgentAiBackend? AiBackend { get; set; }

    public Uri? EndpointUri
    {
        get
        {
            lock (_lifecycleLock)
                return _endpointUri;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock)
                return _application is not null;
        }
    }

    // The settings page installs a shared URL when its owner has proved it belongs to this
    // profile. EndpointUri stays process-specific for authenticated instance routing.
    public Uri? ConnectionUri
    {
        get
        {
            lock (_lifecycleLock)
                return _connectionUri;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        Task startup;
        TaskCompletionSource? completion = null;
        CancellationToken startupToken = default;
        lock (_lifecycleLock)
        {
            // This endpoint is a one-shot application-lifetime resource. Sharing the startup task
            // makes concurrent callers observe the same result and gives StopAsync something
            // concrete to join before project services are torn down.
            if (_stopRequested)
                return Task.CompletedTask;

            if (_startupTask is null)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _startupTask = completion.Task;
                startupToken = _startupCancellation.Token;
            }
            startup = _startupTask;
        }

        if (completion is not null)
            _ = Task.Run(() => CompleteStartupAsync(startupToken, completion));
        return cancellationToken.CanBeCanceled
            ? startup.WaitAsync(cancellationToken)
            : startup;
    }

    private async Task CompleteStartupAsync(CancellationToken token, TaskCompletionSource completion)
    {
        try
        {
            await StartCoreAsync(token).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_beforeStart is { } beforeStart)
            await beforeStart(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (_tokenFactory is { } tokenFactory)
        {
            // This path runs on the shared startup task, away from editor construction.
            // Store failures are observed by StartInBackground without stopping the editor.
            string token = tokenFactory();
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(_config.LiveMcpToken))
            {
                // Only remove the legacy copy after durable publication. Configuration events
                // and their auto-save must stay on the UI thread with other settings changes.
                await Dispatcher.UIThread.InvokeAsync(() => _config.LiveMcpToken = "",
                    DispatcherPriority.Normal, cancellationToken);
            }
            Token = token;
            _instanceRouter = CreateInstanceRouter(token);
        }

        int port = _preferredPort;
        while (true)
        {
            var (app, failover) = CreateApplication(port);

            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);

                IServerAddressesFeature addresses = app.Services
                    .GetRequiredService<IServer>()
                    .Features
                    .Get<IServerAddressesFeature>()!;
                string address = addresses.Addresses.Single();

                var endpointUri = new Uri(new Uri(address), "/mcp");
                Uri connectionUri = await failover.ResolveConnectionUriAsync(endpointUri, cancellationToken).ConfigureAwait(false);

                bool stopRequested;
                lock (_lifecycleLock)
                {
                    stopRequested = _stopRequested;
                    if (!stopRequested)
                    {
                        try { _instanceRegistration = _instanceRegistry.Register(InstanceId, endpointUri); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            s_logger.LogWarning(ex,
                                "Could not register the agent host for discovery. Direct live MCP access remains available.");
                        }
                        _application = app;
                        // Publish EndpointUri only after the stop check: TakeApplication already
                        // cleared it (while still null), so setting it before this check would leave
                        // a dead URL visible to the settings page after a stop-during-startup race.
                        _endpointUri = endpointUri;
                        _connectionUri = connectionUri;
                        _failover = failover;
                        _failoverTask = Task.Run(() => failover.RunAsync(addresses, endpointUri, uri =>
                        {
                            lock (_lifecycleLock)
                            {
                                if (!_stopRequested)
                                    _connectionUri = uri;
                            }
                        }, cancellationToken));
                    }
                }

                // StopAsync ran while app.StartAsync was in flight (so it couldn't see/take
                // _application): stop the just-started host here instead of leaving it running.
                if (stopRequested)
                {
                    try { await StopAndDisposeWithTimeoutAsync(app).ConfigureAwait(false); }
                    finally { failover.Dispose(); }
                }

                return;
            }
            catch (Exception ex) when (IsAddressInUse(ex))
            {
                try { await app.DisposeAsync().ConfigureAwait(false); }
                finally { failover.Dispose(); }
                if (port >= IPEndPoint.MaxPort)
                {
                    throw;
                }

                port++;
            }
            catch
            {
                try { await app.DisposeAsync().ConfigureAwait(false); }
                finally { failover.Dispose(); }
                throw;
            }
        }
    }

    public void StartInBackground()
    {
        _ = ObserveBackgroundStartAsync(StartAsync());
    }

    private async Task ObserveBackgroundStartAsync(Task startup)
    {
        try
        {
            await startup.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogError(
                ex,
                "The agent host endpoint failed to start; the live MCP endpoint is unavailable.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stop;
        lock (_lifecycleLock)
        {
            _stopRequested = true;
            _endpointUri = null;
            _connectionUri = null;
            _instanceRegistration?.Dispose();
            _instanceRegistration = null;
            WebApplication? application = _application;
            _application = null;
            AgentHostEndpointFailover? failover = _failover;
            _failover = null;
            stop = _stopTask ??= StopCoreAsync(application, failover, _failoverTask);
        }

        _extensionTools.Dispose();
        try
        {
            _startupCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return cancellationToken.CanBeCanceled
            ? stop.WaitAsync(cancellationToken)
            : stop;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _startupCancellation.Dispose();
    }

    private async Task StopCoreAsync(WebApplication? app, AgentHostEndpointFailover? failover, Task? failoverTask)
    {
        Task? startup;
        lock (_lifecycleLock)
            startup = _startupTask;

        if (startup is not null)
        {
            try
            {
                await startup.WaitAsync(s_shutdownTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested)
            {
            }
            catch (TimeoutException)
            {
                s_logger.LogWarning(
                    "Timed out waiting for the agent host startup path during shutdown.");
            }
            catch (Exception ex)
            {
                // StartAsync exposes the startup failure to its caller. Shutdown still has to
                // continue and dispose any partially-created host.
                s_logger.LogWarning(ex, "The agent host startup failed before shutdown completed.");
            }
        }

        if (app is not null)
        {
            if (failoverTask is not null)
            {
                try { await failoverTask.WaitAsync(s_shutdownTimeout).ConfigureAwait(false); }
                catch (TimeoutException) { s_logger.LogWarning("Timed out stopping live MCP port failover."); }
            }
            try { await StopAndDisposeWithTimeoutAsync(app).ConfigureAwait(false); }
            finally { failover?.Dispose(); }
        }
    }

    private (WebApplication App, AgentHostEndpointFailover Failover) CreateApplication(int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(AgentHostEndpoint).Assembly.FullName
        });

        var failover = new AgentHostEndpointFailover(_instanceRegistry, Token, InstanceId, _preferredPort);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
            options.Configure(failover.Configuration, reloadOnChange: true);
        });

        string workspaceRoot = ResolveWorkspaceRoot(_config);

        builder.Services
            .AddSingleton(_projectService)
            .AddSingleton(_editorService)
            .AddSingleton(InstanceRouter)
            .AddSingleton<EditorProjectSessionGateway>()
            .AddSingleton<IProjectSessionGateway>(services => services.GetRequiredService<EditorProjectSessionGateway>())
            .AddSingleton<CompositionPlanStore>()
            .AddScoped<AgentSessionManager>()
            .AddSingleton<IWorkspaceGuard>(_ => new WorkspaceGuard(workspaceRoot))
            .AddSingleton<IOutputOperationLeaseProvider>(_ => _editorService)
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<StillRenderer>()
            .AddSingleton<StoryboardRenderer>()
            .AddSingleton<FrameDifferenceAnalyzer>()
            .AddSingleton<AudioRhythmAnalyzer>()
            .AddSingleton<EncoderRegistration>()
            .AddSingleton<VideoExporter>()
            .AddSingleton<RenderJobManager>()
            .AddSingleton<AgentAiJobManager>()
            .AddSingleton<IAgentAiBackend>(_ => AiBackend ?? UnavailableAgentAiBackend.Instance);

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = true;
                options.ConfigureSessionOptions = (_, serverOptions, _) =>
                {
                    _extensionTools.AddTo(serverOptions);
                    return Task.CompletedTask;
                };
            })
            .WithRequestFilters(filters =>
            {
                AgentHostInstanceRouter.AddFilters(filters);
                AgentHostSceneRouter.AddFilters(filters);
                filters.AddCallToolFilter(next => (context, cancellationToken) =>
                    _extensionTools.TryCreateRemovedToolResult(context, out var removed)
                        ? ValueTask.FromResult(removed)
                        : next(context, cancellationToken));
                filters.AddToolkitCallToolErrorFilter();
            })
            .WithTools<AgentHostInstanceTools>()
            .WithTools<AgentHostTools>()
            .WithTools<SessionTools>()
            .WithTools<QueryTools>()
            .WithTools<EditTools>()
            .WithTools<HistoryTools>()
            .WithTools<RenderTools>()
            .WithTools<AgentHostAiTools>();

        WebApplication app;
        try { app = builder.Build(); }
        catch
        {
            failover.Dispose();
            throw;
        }
        app.Use(RequireToken);
        app.MapGet("/agent-host/identity", (string? challenge) =>
            AgentHostInstanceAuthentication.IsValidChallenge(challenge)
                ? Results.Json(InstanceRouter.CreateIdentityProof(challenge!))
                : Results.BadRequest());
        app.MapGet("/agent-host", (CancellationToken cancellationToken) => InstanceRouter.GetInfoAsync(cancellationToken));
        app.MapMcp("/mcp");
        return (app, failover);
    }

    private static async Task StopAndDisposeWithTimeoutAsync(WebApplication app)
    {
        using var cts = new CancellationTokenSource(s_shutdownTimeout);

        try
        {
            await StopAndDisposeAsync(app, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "The agent host endpoint failed to stop cleanly.");
        }
    }

    private static async Task StopAndDisposeAsync(WebApplication app, CancellationToken cancellationToken)
    {
        try
        {
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
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

        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/agent-host/identity")
        {
            if (context.Request.Headers.TryGetValue(AgentHostInstanceRouter.InstanceHeader, out var expectedId)
                && !string.Equals(expectedId, InstanceId, StringComparison.Ordinal))
                context.Response.StatusCode = StatusCodes.Status409Conflict;
            else
                await next(context).ConfigureAwait(false);
            return;
        }

        if (authorization is null
            || !authorization.StartsWith(scheme, StringComparison.Ordinal)
            || !(FixedTimeTokenEquals(authorization[scheme.Length..], Token)
                 || InstanceRouter.IsForwardToken(authorization[scheme.Length..])))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (context.Request.Headers.TryGetValue(AgentHostInstanceRouter.InstanceHeader, out var instanceId)
            && !string.Equals(instanceId, InstanceId, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        await next(context).ConfigureAwait(false);
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
