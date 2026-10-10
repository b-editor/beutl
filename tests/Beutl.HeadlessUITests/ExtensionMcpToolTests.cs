using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Live;
using Beutl.Api.Services;
using Beutl.Editor.Services.Mcp;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class ExtensionMcpToolTests
{
    private const string Token = "extension-tool-token";

    [AvaloniaTest]
    public async Task Extension_tool_is_listed_and_runs_on_the_ui_thread_with_the_selected_editor()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        // The shared provider carries the scene editor extension that opening a tab needs.
        ExtensionProvider provider = TestShell.Extensions;
        var editor = new EditorService(provider);
        Scene scene = OpenScene(editor, directory, "extension-scene");
        bool? ranOnUIThread = null;
        object? editedObject = null;
        provider.AddExtensions(-45001, [new ToolExtension(
            [
                new McpToolDefinition("test.echo", "Echoes a message.", Schema(
                    """{"type":"object","properties":{"message":{"type":"string"}},"required":["message"]}"""))
                {
                    Title = "Echo",
                    ReadOnlyHint = true
                }
            ],
            call =>
            {
                ranOnUIThread = Dispatcher.UIThread.CheckAccess();
                editedObject = call.EditorContext?.Object;
                var value = new JsonObject
                {
                    ["tool"] = call.Name,
                    ["echo"] = call.Arguments.GetProperty("message").GetString()
                };
                return new(McpToolResult.Json(JsonSerializer.SerializeToElement(value)));
            })]);
        await using AgentHostEndpoint host = CreateHost(editor, directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            McpClientTool tool = (await client.ListToolsAsync()).Single(tool => tool.Name == "test.echo");
            CallToolResult result = await client.CallToolAsync(
                "test.echo", new Dictionary<string, object?> { ["message"] = "hello" });
            CallToolResult routed = await client.CallToolAsync(
                "test.echo", new Dictionary<string, object?> { ["message"] = "routed", ["instanceId"] = host.InstanceId });
            CallToolResult rejected = await client.CallToolAsync(
                "test.echo", new Dictionary<string, object?> { ["message"] = "hello", ["unknown"] = 1 });

            Assert.Multiple(() =>
            {
                Assert.That(tool.Description, Is.EqualTo("Echoes a message."));
                Assert.That(tool.ProtocolTool.Title, Is.EqualTo("Echo"));
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint, Is.True);
                Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint, Is.Null);
                Assert.That(tool.JsonSchema.GetProperty("properties").TryGetProperty("instanceId", out _), Is.True);
                Assert.That(result.IsError, Is.False);
                Assert.That(result.StructuredContent?.GetProperty("echo").GetString(), Is.EqualTo("hello"));
                Assert.That(result.StructuredContent?.GetProperty("tool").GetString(), Is.EqualTo("test.echo"));
                Assert.That(Text(result), Does.Contain("\"echo\":\"hello\""));
                Assert.That(routed.StructuredContent?.GetProperty("echo").GetString(), Is.EqualTo("routed"));
                Assert.That(ErrorCodeOf(rejected), Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ranOnUIThread, Is.True);
                Assert.That(editedObject, Is.SameAs(scene));
            });
        }
        finally
        {
            await host.StopAsync();
            await provider.RemoveExtensions(-45001).DrainAsync();
            await editor.CloseTabItem(editor.SelectedTabItem.Value!, saveChanges: false);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Extension_tools_follow_package_registration_without_restarting_the_endpoint()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var provider = new ExtensionProvider();
        await using AgentHostEndpoint host = CreateHost(new EditorService(provider), directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            Assert.That((await client.ListToolsAsync()).Select(tool => tool.Name), Does.Not.Contain("test.dynamic"));
            // The installed server learns about tool changes from the registry's tools version.
            var registry = new AgentHostInstanceRegistry(directory);
            Assert.That(registry.Read().Single().ToolsVersion, Is.EqualTo(0));

            provider.AddExtensions(-45002, [new ToolExtension(
                [new McpToolDefinition("test.dynamic", "Appears at runtime.")],
                _ => new(McpToolResult.Text("dynamic")))]);
            IList<McpClientTool> added = await client.ListToolsAsync();
            CallToolResult result = await client.CallToolAsync("test.dynamic");
            Assert.Multiple(() =>
            {
                Assert.That(added.Select(tool => tool.Name), Does.Contain("test.dynamic"));
                Assert.That(Text(result), Is.EqualTo("dynamic"));
                Assert.That(registry.Read().Single().ToolsVersion, Is.EqualTo(1));
            });

            await provider.RemoveExtensions(-45002).DrainAsync();
            IList<McpClientTool> removed = await client.ListToolsAsync();
            CallToolResult stale = await client.CallToolAsync("test.dynamic");
            Assert.Multiple(() =>
            {
                Assert.That(removed.Select(tool => tool.Name), Does.Not.Contain("test.dynamic"));
                Assert.That(stale.IsError, Is.True);
                Assert.That(ErrorCodeOf(stale), Is.EqualTo(ErrorCode.ExtensionToolUnavailable));
                Assert.That(registry.Read().Single().ToolsVersion, Is.EqualTo(2));
            });
        }
        finally
        {
            await host.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Package_removal_waits_for_a_running_extension_tool_call()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var provider = new ExtensionProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AddExtensions(-45003, [new ToolExtension(
            [new McpToolDefinition("test.slow", "Waits until released.")],
            async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return McpToolResult.Text("finished");
            })]);
        await using AgentHostEndpoint host = CreateHost(new EditorService(provider), directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            Task<CallToolResult> call = client.CallToolAsync("test.slow").AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Task drain = provider.RemoveExtensions(-45003).DrainAsync().AsTask();
            await Task.Delay(100);
            Assert.That(drain.IsCompleted, Is.False);

            release.TrySetResult();
            Assert.That(Text(await call.WaitAsync(TimeSpan.FromSeconds(10))), Is.EqualTo("finished"));
            await drain.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.TrySetResult();
            await host.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Built_in_tools_keep_closed_argument_schemas()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        await using AgentHostEndpoint host = CreateHost(new EditorService(new ExtensionProvider()), directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            IList<McpClientTool> tools = await client.ListToolsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(tools, Is.Not.Empty);
                foreach (McpClientTool tool in tools)
                {
                    foreach (string keyword in new[] { "patternProperties", "allOf", "anyOf", "oneOf", "if", "then", "else", "dependentSchemas", "$ref", "$dynamicRef" })
                        Assert.That(tool.JsonSchema.TryGetProperty(keyword, out _), Is.False, $"{tool.Name}: {keyword}");
                    if (tool.JsonSchema.TryGetProperty("additionalProperties", out JsonElement additional))
                        Assert.That(additional.ValueKind, Is.EqualTo(JsonValueKind.False), tool.Name);
                }
            });
        }
        finally
        {
            await host.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task An_extension_whose_tool_list_throws_does_not_hide_other_tools()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var provider = new ExtensionProvider();
        provider.AddExtensions(-45008, [new ToolExtension(new ThrowingToolList(), _ => new(McpToolResult.Text("never")))]);
        provider.AddExtensions(-45009, [new ToolExtension(
            [new McpToolDefinition("test.healthy", "Still listed.")],
            _ => new(McpToolResult.Text("healthy")))]);
        await using AgentHostEndpoint host = CreateHost(new EditorService(provider), directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            Assert.That((await client.ListToolsAsync()).Select(tool => tool.Name), Does.Contain("test.healthy"));
        }
        finally
        {
            await host.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Conflicting_and_reserved_extension_tools_are_skipped()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var provider = new ExtensionProvider();
        provider.AddExtensions(-45004, [new ToolExtension(
            [
                new McpToolDefinition("undo", "Shadows a built-in tool."),
                new McpToolDefinition("test.shared", "First provider.")
            ],
            _ => new(McpToolResult.Text("first")))]);
        provider.AddExtensions(-45005, [new ToolExtension(
            [
                new McpToolDefinition("test.shared", "Second provider."),
                new McpToolDefinition("test.reserved", "Declares the routing argument.", Schema(
                    """{"type":"object","properties":{"instanceId":{"type":"string"}}}"""))
            ],
            _ => new(McpToolResult.Text("second")))]);
        await using AgentHostEndpoint host = CreateHost(new EditorService(provider), directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            IList<McpClientTool> tools = await client.ListToolsAsync();
            CallToolResult shared = await client.CallToolAsync("test.shared");
            CallToolResult undo = await client.CallToolAsync("undo");

            Assert.Multiple(() =>
            {
                Assert.That(tools.Count(tool => tool.Name == "undo"), Is.EqualTo(1));
                Assert.That(tools.Single(tool => tool.Name == "undo").Description, Is.Not.EqualTo("Shadows a built-in tool."));
                Assert.That(tools.Single(tool => tool.Name == "test.shared").Description, Is.EqualTo("First provider."));
                Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain("test.reserved"));
                Assert.That(Text(shared), Is.EqualTo("first"));
                Assert.That(Text(undo), Does.Not.Contain("first"));
            });
        }
        finally
        {
            await host.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Extension_results_and_failures_reach_the_agent()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var provider = new ExtensionProvider();
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        provider.AddExtensions(-45006, [new ToolExtension(
            [
                new McpToolDefinition("test.image", "Returns an image."),
                new McpToolDefinition("test.reject", "Reports an error."),
                new McpToolDefinition("test.throw", "Throws.")
            ],
            call => call.Name switch
            {
                "test.image" => new(McpToolResult.Image(png, "image/png")),
                "test.reject" => new(McpToolResult.Error("bad input")),
                _ => throw new InvalidOperationException("boom")
            })]);
        await using AgentHostEndpoint host = CreateHost(new EditorService(provider), directory);
        try
        {
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            CallToolResult image = await client.CallToolAsync("test.image");
            CallToolResult rejected = await client.CallToolAsync("test.reject");
            CallToolResult thrown = await client.CallToolAsync("test.throw");

            Assert.Multiple(() =>
            {
                ImageContentBlock block = image.Content.OfType<ImageContentBlock>().Single();
                Assert.That(block.MimeType, Is.EqualTo("image/png"));
                Assert.That(block.DecodedData.ToArray(), Is.EqualTo(png));
                Assert.That(rejected.IsError, Is.True);
                Assert.That(Text(rejected), Is.EqualTo("bad input"));
                Assert.That(thrown.IsError, Is.True);
                Assert.That(ErrorCodeOf(thrown), Is.EqualTo(ErrorCode.ExtensionToolFailed));
                Assert.That(Text(thrown), Does.Contain("boom"));
            });
        }
        finally
        {
            await host.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Removed_extension_can_unload_while_its_tool_snapshot_is_retained()
    {
        var (catalog, tool, assembly) = CreateRetiredCollectibleCatalog();
        for (int i = 0; i < 20 && assembly.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }

        Assert.Multiple(() =>
        {
            Assert.That(assembly.IsAlive, Is.False);
            Assert.That(catalog.Tools, Is.Empty);
            Assert.That(tool.ProtocolTool.Name, Is.EqualTo("test.collectible"));
        });
        catalog.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ExtensionMcpToolCatalog Catalog, ExtensionMcpTool Tool, WeakReference Assembly) CreateRetiredCollectibleCatalog()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("McpToolCollectible" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        Type type = assembly.DefineDynamicModule("McpTool")
            .DefineType("CollectibleTool", TypeAttributes.Public, typeof(CollectibleToolExtensionBase)).CreateType()!;
        var provider = new ExtensionProvider();
        provider.AddExtensions(-45007, [(Extension)Activator.CreateInstance(type)!]);
        var catalog = new ExtensionMcpToolCatalog(new EditorService(provider));
        ExtensionMcpTool tool = catalog.Tools.Single();
        provider.RemoveExtensions(-45007).DrainAsync().AsTask().GetAwaiter().GetResult();
        return (catalog, tool, new WeakReference(type.Assembly));
    }

    public class CollectibleToolExtensionBase : McpToolExtension
    {
        public override IReadOnlyList<McpToolDefinition> Tools =>
            [new McpToolDefinition("test.collectible", "Lives in a collectible assembly.")];

        public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
            => new(McpToolResult.Text("collectible"));
    }

    private sealed class ToolExtension(
        IReadOnlyList<McpToolDefinition> tools,
        Func<McpToolCall, ValueTask<McpToolResult>> invoke) : McpToolExtension
    {
        public override IReadOnlyList<McpToolDefinition> Tools => tools;

        public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
            => invoke(call);
    }

    private sealed class ThrowingToolList : IReadOnlyList<McpToolDefinition>
    {
        public int Count => 1;

        public McpToolDefinition this[int index] => throw new InvalidOperationException("broken list");

        public IEnumerator<McpToolDefinition> GetEnumerator() => throw new InvalidOperationException("broken list");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    private static string Text(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string? ErrorCodeOf(CallToolResult result)
        => JsonNode.Parse(Text(result))?["error"]?["code"]?.GetValue<string>();

    private static AgentHostEndpoint CreateHost(EditorService editor, string directory)
        => new(new ProjectService(), editor, AvailablePort(), Token, registryDirectory: directory);

    private static async Task<McpClient> ConnectAsync(AgentHostEndpoint endpoint)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint.EndpointUri!,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Token }
        }));

    private static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-extension-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static Scene OpenScene(EditorService editor, string directory, string name)
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
