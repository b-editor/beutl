using System.Net;
using System.Security.Cryptography;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
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
            .WithToolsFromAssembly(typeof(AgentToolkitAssembly).Assembly)
            .WithToolsFromAssembly(typeof(AgentHostEndpoint).Assembly);

        WebApplication app = builder.Build();
        app.Use(RequireToken);
        app.MapMcp();

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
        WebApplication? app = _application;
        _application = null;
        EndpointUri = null;

        if (app is not null)
        {
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
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
