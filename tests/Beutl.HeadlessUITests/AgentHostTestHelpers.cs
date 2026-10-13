using System.Net;
using System.Net.Sockets;
using Beutl.AgentHost;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using ModelContextProtocol.Client;

namespace Beutl.HeadlessUITests;

// Shared setup for the tests that run an agent host and call it over MCP.
internal static class AgentHostTestHelpers
{
    // A host that registers itself in directory, on firstPort or else on a free port.
    public static AgentHostEndpoint CreateHost(EditorService editor, string directory, string token, int? firstPort = null)
        => new(new ProjectService(), editor, firstPort ?? AvailablePort(), token, registryDirectory: directory);

    public static Task<McpClient> ConnectAsync(AgentHostEndpoint host) => ConnectAsync(host.EndpointUri!, host.Token);

    public static async Task<McpClient> ConnectAsync(Uri endpoint, string token)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
        }));

    public static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // A new folder in the temporary folder whose name starts with prefix.
    public static string CreateDirectory(string prefix)
    {
        string directory = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static Scene OpenScene(EditorService editor, string directory, string name)
    {
        var scene = new Scene
        {
            Name = name,
            Uri = new Uri(Path.Combine(directory, name + ".scene")),
            Duration = TimeSpan.FromSeconds(1),
            FrameSize = new PixelSize(640, 480)
        };
        editor.ActivateTabItem(scene);
        Assert.That(editor.SelectedTabItem.Value, Is.Not.Null);
        HeadlessTestHelpers.Settle();
        return scene;
    }
}
