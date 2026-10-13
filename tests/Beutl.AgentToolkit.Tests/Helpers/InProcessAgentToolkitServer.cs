using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tests.Helpers;

// The installed server as Program.cs wires it, minus the hosted watcher, over an in-memory stdio pair.
internal sealed class InProcessAgentToolkitServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly Stream[] _streams;

    private InProcessAgentToolkitServer(IHost host, McpClient client, Stream[] streams)
    {
        _host = host;
        Client = client;
        _streams = streams;
    }

    public McpClient Client { get; }

    public IServiceProvider Services => _host.Services;

    public static async Task<InProcessAgentToolkitServer> StartAsync(
        string profileDirectory,
        Action<IServiceCollection>? configure = null)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        Stream clientOutput = clientToServer.Writer.AsStream();
        Stream serverInput = clientToServer.Reader.AsStream();
        Stream serverOutput = serverToClient.Writer.AsStream();
        Stream clientInput = serverToClient.Reader.AsStream();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddAgentToolkitServer(profileDirectory);
        configure?.Invoke(builder.Services);
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(serverInput, serverOutput)
            .WithAgentToolkitTools();

        IHost host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientOutput, clientInput))
            .ConfigureAwait(false);
        return new InProcessAgentToolkitServer(host, client, [clientOutput, serverInput, serverOutput, clientInput]);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
        foreach (Stream stream in _streams)
        {
            stream.Dispose();
        }
    }
}
