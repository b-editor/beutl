using System.Net;
using System.Security.Cryptography;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

public sealed class AgentHostEndpoint : IAsyncDisposable
{
    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly EditorService _editorService;
    private WebApplication? _application;

    public AgentHostEndpoint(EditorService editorService)
    {
        _editorService = editorService;
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
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

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(AgentHostEndpoint).Assembly.FullName
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
        });

        string workspaceRoot = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE")
                               ?? Directory.GetCurrentDirectory();

        builder.Services
            .AddSingleton(_editorService)
            .AddSingleton<LiveSessionSource>()
            .AddSingleton<AgentSessionManager>()
            .AddSingleton<IWorkspaceGuard>(_ => new WorkspaceGuard(workspaceRoot))
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<StillRenderer>()
            .AddSingleton<EncoderRegistration>()
            .AddSingleton<VideoExporter>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<AgentHostTools>()
            .WithTools<QueryTools>()
            .WithTools<EditTools>()
            .WithTools<RenderTools>();

        WebApplication app = builder.Build();
        app.Use(RequireToken);
        app.MapMcp("/mcp");

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        string address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Single();

        EndpointUri = new Uri(new Uri(address), "/mcp");
        _application = app;
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

    private async Task RequireToken(HttpContext context, RequestDelegate next)
    {
        string? header = context.Request.Headers["X-Beutl-Agent-Token"];
        string? query = context.Request.Query["token"];

        if (!string.Equals(header, Token, StringComparison.Ordinal)
            && !string.Equals(query, Token, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
