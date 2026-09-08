using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Configuration;
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
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _startupCancellation = new();
    private bool _stopRequested;
    private WebApplication? _application;
    private Uri? _endpointUri;
    private Task? _startupTask;
    private Task? _stopTask;

    public AgentHostEndpoint(ProjectService projectService, EditorService editorService)
        : this(projectService, editorService, GlobalConfiguration.Instance.AiAgentConfig)
    {
    }

    internal AgentHostEndpoint(ProjectService projectService, EditorService editorService, AiAgentConfig config)
        : this(projectService, editorService, DefaultPort, ResolveToken(config), config)
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

    internal AgentHostEndpoint(
        ProjectService projectService,
        EditorService editorService,
        int preferredPort,
        string token,
        Func<CancellationToken, Task>? beforeStart = null)
        : this(
            projectService,
            editorService,
            preferredPort,
            token,
            GlobalConfiguration.Instance.AiAgentConfig,
            beforeStart)
    {
    }

    private AgentHostEndpoint(
        ProjectService projectService,
        EditorService editorService,
        int preferredPort,
        string token,
        AiAgentConfig config,
        Func<CancellationToken, Task>? beforeStart = null)
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
        _beforeStart = beforeStart;
        Token = token;
    }

    public string Token { get; }

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

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        Task startup;
        lock (_lifecycleLock)
        {
            // This endpoint is a one-shot application-lifetime resource. Sharing the startup task
            // makes concurrent callers observe the same result and gives StopAsync something
            // concrete to join before project services are torn down.
            if (_stopRequested)
                return Task.CompletedTask;

            startup = _startupTask ??= StartCoreAsync(_startupCancellation.Token);
        }

        return cancellationToken.CanBeCanceled
            ? startup.WaitAsync(cancellationToken)
            : startup;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_beforeStart is { } beforeStart)
            await beforeStart(cancellationToken).ConfigureAwait(false);

        int port = _preferredPort;
        while (true)
        {
            WebApplication app = CreateApplication(port);

            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);

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
                    if (!stopRequested)
                    {
                        _application = app;
                        // Publish EndpointUri only after the stop check: TakeApplication already
                        // cleared it (while still null), so setting it before this check would leave
                        // a dead URL visible to the settings page after a stop-during-startup race.
                        _endpointUri = endpointUri;
                    }
                }

                // RequestStop ran while app.StartAsync was in flight (so it couldn't see/take
                // _application): stop the just-started host here instead of leaving it running.
                if (stopRequested)
                {
                    await StopAndDisposeWithTimeoutAsync(app).ConfigureAwait(false);
                }

                return;
            }
            catch (Exception ex) when (IsAddressInUse(ex))
            {
                await app.DisposeAsync().ConfigureAwait(false);
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

    public void StartInBackground()
    {
        Task startup;
        lock (_lifecycleLock)
        {
            if (_stopRequested)
                return;

            startup = _startupTask ??= Task.Run(
                () => StartCoreAsync(_startupCancellation.Token));
        }

        _ = ObserveBackgroundStartAsync(startup);
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
            stop = _stopTask ??= StopCoreAsync();
        }

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

    public void RequestStop()
    {
        _ = StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _startupCancellation.Dispose();
    }

    private async Task StopCoreAsync()
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

        WebApplication? app;
        lock (_lifecycleLock)
        {
            app = _application;
            _application = null;
            _endpointUri = null;
        }

        if (app is not null)
        {
            await StopAndDisposeWithTimeoutAsync(app).ConfigureAwait(false);
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
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<StillRenderer>()
            .AddSingleton<StoryboardRenderer>()
            .AddSingleton<MotionVariationAnalyzer>()
            .AddSingleton<AudioRhythmAnalyzer>()
            .AddSingleton<QualityAnalyzer>()
            .AddSingleton<EncoderRegistration>()
            .AddSingleton<VideoExporter>()
            .AddSingleton<RenderJobManager>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithRequestFilters(filters => filters.AddToolkitCallToolErrorFilter())
            .WithTools<AgentHostTools>()
            .WithTools<SessionTools>()
            .WithTools<QueryTools>()
            .WithTools<DesignTools>()
            .WithTools<EditTools>()
            .WithTools<HistoryTools>()
            .WithTools<RenderTools>();

        WebApplication app = builder.Build();
        app.Use(RequireToken);
        app.MapMcp("/mcp");
        return app;
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

        if (authorization is null
            || !authorization.StartsWith(scheme, StringComparison.Ordinal)
            || !FixedTimeTokenEquals(authorization[scheme.Length..], Token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
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
