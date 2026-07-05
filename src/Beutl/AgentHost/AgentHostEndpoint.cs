using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Configuration;
using Beutl.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed class AgentHostEndpoint : IAsyncDisposable
{
    internal const int DefaultPort = 59737;

    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly int _preferredPort;
    private WebApplication? _application;

    public AgentHostEndpoint(ProjectService projectService, EditorService editorService)
        : this(projectService, editorService, GlobalConfiguration.Instance.AiAgentConfig)
    {
    }

    internal AgentHostEndpoint(ProjectService projectService, EditorService editorService, AiAgentConfig config)
        : this(projectService, editorService, DefaultPort, ResolveToken(config))
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
    // restart picks up a change) over BEUTL_WORKSPACE / the process directory.
    private static string ResolveWorkspaceRoot()
    {
        string configured = GlobalConfiguration.Instance.AiAgentConfig.WorkspaceRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string? env = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE");
        return string.IsNullOrWhiteSpace(env) ? Directory.GetCurrentDirectory() : env;
    }

    internal AgentHostEndpoint(ProjectService projectService, EditorService editorService, int preferredPort, string token)
    {
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
        _preferredPort = preferredPort;
        Token = token;
    }

    public string Token { get; }

    public Uri? EndpointUri { get; private set; }

    public bool IsRunning => _application is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

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

                EndpointUri = new Uri(new Uri(address), "/mcp");
                _application = app;
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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        WebApplication? app = TakeApplication();

        if (app is not null)
        {
            await StopAndDisposeAsync(app, cancellationToken).ConfigureAwait(false);
        }
    }

    public void RequestStop()
    {
        WebApplication? app = TakeApplication();
        if (app is not null)
        {
            _ = StopAndDisposeWithTimeoutAsync(app);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
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

        string workspaceRoot = ResolveWorkspaceRoot();

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
            .WithTools<RenderTools>();

        WebApplication app = builder.Build();
        app.Use(RequireToken);
        app.MapMcp("/mcp");
        return app;
    }

    private WebApplication? TakeApplication()
    {
        WebApplication? app = Interlocked.Exchange(ref _application, null);
        EndpointUri = null;
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
            _ = ex;
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
            || !string.Equals(authorization[scheme.Length..], Token, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
