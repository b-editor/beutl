using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tests.Live;

// The installed server without any running editor: headless tools keep working like the former
// stdio server, live targeting is described and validated, and nothing falls back silently.
// Routing to real Beutl processes is covered by the headless UI tests, which can start editors.
[TestFixture]
public sealed class LiveMcpBrokerTests
{
    private string _root = null!;

    private string Workspace => Path.Combine(_root, "workspace");

    private string Registry => Path.Combine(_root, "agent-hosts");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "beutl-agent-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(Registry);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public async Task Tools_list_adds_live_targeting_to_headless_tools_and_offers_discovery()
    {
        await using InProcessServer server = await InProcessServer.StartAsync(Workspace, Registry, () => "test-token");

        IList<McpClientTool> tools = await server.Client.ListToolsAsync();
        string[] names = tools.Select(tool => tool.Name).ToArray();
        McpClientTool applyEdit = tools.Single(tool => tool.Name == "apply_edit");
        JsonElement applyEditProperties = applyEdit.JsonSchema.GetProperty("properties");
        // add_scene is the headless tool whose file session handle is required.
        McpClientTool addScene = tools.Single(tool => tool.Name == "add_scene");
        JsonElement addSceneProperties = addScene.JsonSchema.GetProperty("properties");
        string?[] addSceneRequired = addScene.JsonSchema.TryGetProperty("required", out JsonElement requiredElement)
            ? requiredElement.EnumerateArray().Select(item => item.GetString()).ToArray()
            : [];
        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain(LiveMcpBroker.ListInstancesToolName)
                .And.Contain("create_project").And.Contain("read_document").And.Contain("apply_edit"));
            Assert.That(names.Count(name => name == LiveMcpBroker.ListInstancesToolName), Is.EqualTo(1));
            // Editor-only tools are described only while an editor runs.
            Assert.That(names, Does.Not.Contain("list_scenes"));
            // Every headless tool also accepts a live target; the file session is then optional.
            Assert.That(applyEditProperties.TryGetProperty("instanceId", out _), Is.True);
            Assert.That(applyEditProperties.TryGetProperty("sceneId", out _), Is.True);
            Assert.That(addSceneProperties.TryGetProperty("instanceId", out _), Is.True);
            Assert.That(addSceneProperties.GetProperty("session").GetProperty("description").GetString(), Does.Contain("Headless only"));
            Assert.That(addSceneRequired, Does.Not.Contain("session"));
            Assert.That(tools.Single(tool => tool.Name == LiveMcpBroker.ListInstancesToolName).JsonSchema
                .GetProperty("properties").TryGetProperty("instanceId", out _), Is.False);
        });
    }

    [Test]
    public async Task Calls_without_instanceId_edit_project_files_like_the_former_stdio_server()
    {
        await using InProcessServer server = await InProcessServer.StartAsync(Workspace, Registry, () => "test-token");

        AssertSuccess(await server.Client.CallToolAsync("create_project", new Dictionary<string, object?>
        {
            ["path"] = "headless.bep",
            ["width"] = 32,
            ["height"] = 18,
            ["frameRate"] = 30,
            ["duration"] = "00:00:01"
        }));
        JsonObject status = Payload(await server.Client.CallToolAsync("read_operation_status"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(Workspace, "headless.bep")), Is.True);
            Assert.That(status["isSuccess"]!.GetValue<bool>(), Is.True, status.ToJsonString());
            Assert.That(status["value"]!["hasActiveSession"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public async Task Live_calls_are_validated_and_never_fall_back_to_the_headless_editor()
    {
        await using InProcessServer server = await InProcessServer.StartAsync(Workspace, Registry, () => "test-token");
        McpClient client = server.Client;
        string unknown = Guid.NewGuid().ToString("N");

        AssertError(await client.CallToolAsync("apply_edit", new Dictionary<string, object?>
        {
            ["instanceId"] = unknown,
            ["sceneId"] = Guid.NewGuid().ToString(),
            ["schemaVersion"] = "1",
            ["patch"] = new JsonObject { ["Name"] = "never applied" }
        }), ErrorCode.InstanceUnavailable);
        AssertError(await client.CallToolAsync("read_document",
            new Dictionary<string, object?> { ["instanceId"] = "not-an-id" }), ErrorCode.ValidationRejected);
        AssertError(await client.CallToolAsync("read_document",
            new Dictionary<string, object?> { ["instanceId"] = 42 }), ErrorCode.ValidationRejected);
        // A scene belongs to a running editor; without an instance the call is neither live nor headless.
        AssertError(await client.CallToolAsync("read_document",
            new Dictionary<string, object?> { ["sceneId"] = Guid.NewGuid().ToString() }), ErrorCode.ValidationRejected);
        // Editor-only tools are rejected headlessly instead of surfacing an unknown-tool error.
        AssertError(await client.CallToolAsync("list_scenes"), ErrorCode.ValidationRejected);
        AssertError(await client.CallToolAsync(LiveMcpBroker.ListInstancesToolName,
            new Dictionary<string, object?> { ["instanceId"] = unknown }), ErrorCode.ValidationRejected);

        JsonObject discovery = Payload(await client.CallToolAsync(LiveMcpBroker.ListInstancesToolName));
        Assert.Multiple(() =>
        {
            Assert.That(discovery["isSuccess"]!.GetValue<bool>(), Is.True, discovery.ToJsonString());
            Assert.That(discovery["value"]!["connectedInstanceId"], Is.Null);
            Assert.That(discovery["value"]!["instances"]!.AsArray(), Is.Empty);
            Assert.That(Directory.EnumerateFiles(Workspace), Is.Empty, "A rejected live call must not touch the workspace.");
        });
    }

    [Test]
    public async Task An_unreadable_token_store_only_affects_live_calls()
    {
        await using InProcessServer server = await InProcessServer.StartAsync(Workspace, Registry,
            () => throw new InvalidDataException("corrupt token store"));
        McpClient client = server.Client;

        JsonObject discovery = Payload(await client.CallToolAsync(LiveMcpBroker.ListInstancesToolName));
        Assert.Multiple(() =>
        {
            Assert.That(discovery["error"]!["code"]!.GetValue<string>(), Is.EqualTo(ErrorCode.LiveUnavailable));
            Assert.That(discovery["error"]!["message"]!.GetValue<string>(), Does.Contain("corrupt token store"));
        });
        AssertError(await client.CallToolAsync("read_document",
            new Dictionary<string, object?> { ["instanceId"] = Guid.NewGuid().ToString("N") }), ErrorCode.LiveUnavailable);
        AssertSuccess(await client.CallToolAsync("read_operation_status"));
    }

    private static JsonObject Payload(CallToolResult result)
        => JsonNode.Parse(result.Content.OfType<TextContentBlock>().First().Text)!.AsObject();

    private static void AssertSuccess(CallToolResult result)
        => Assert.That(Payload(result)["isSuccess"]!.GetValue<bool>(), Is.True, Payload(result).ToJsonString());

    private static void AssertError(CallToolResult result, string code)
        => Assert.That(Payload(result)["error"]?["code"]?.GetValue<string>(), Is.EqualTo(code), Payload(result).ToJsonString());

    // The installed server, wired exactly like Program.cs but over an in-memory stdio pair and
    // with live routing pointed at a test registry and token instead of the user's profile.
    private sealed class InProcessServer : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly Stream[] _streams;

        private InProcessServer(IHost host, McpClient client, Stream[] streams)
        {
            _host = host;
            Client = client;
            _streams = streams;
        }

        public McpClient Client { get; }

        public static async Task<InProcessServer> StartAsync(string workspace, string registryDirectory, Func<string> tokenProvider)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            Stream clientOutput = clientToServer.Writer.AsStream();
            Stream serverInput = clientToServer.Reader.AsStream();
            Stream serverOutput = serverToClient.Writer.AsStream();
            Stream clientInput = serverToClient.Reader.AsStream();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddAgentToolkitServer(workspace, registryDirectory);
            builder.Services.AddSingleton(new LiveMcpBroker(new AgentHostInstanceRegistry(registryDirectory), tokenProvider));
            builder.Services
                .AddMcpServer()
                .WithStreamServerTransport(serverInput, serverOutput)
                .WithAgentToolkitTools();

            IHost host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientOutput, clientInput)).ConfigureAwait(false);
            return new InProcessServer(host, client, [clientOutput, serverInput, serverOutput, clientInput]);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
            foreach (Stream stream in _streams)
                stream.Dispose();
        }
    }
}
