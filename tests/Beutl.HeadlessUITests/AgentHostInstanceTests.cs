using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentHostInstanceTests
{
    [AvaloniaTest]
    public async Task A_single_connection_controls_separate_processes_and_detects_a_crashed_target()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        try
        {
            int preferredPort = AvailablePort();
            await using Worker first = await Worker.StartAsync(directory, "process-first", preferredPort);
            await using Worker second = await Worker.StartAsync(directory, "process-second", preferredPort);
            Assert.That(first.Process.Id, Is.Not.EqualTo(second.Process.Id));
            await using McpClient client = await ConnectAsync(first.EndpointUri);
            JsonArray instances = Payload(await client.CallToolAsync("list_instances"))["value"]!["instances"]!.AsArray();
            Assert.Multiple(() =>
            {
                Assert.That(instances.Select(info => info!["processId"]!.GetValue<int>()),
                    Is.EquivalentTo(new[] { first.Process.Id, second.Process.Id }));
                Assert.That(instances.Select(info => info!["projectName"]!.GetValue<string>()),
                    Is.EquivalentTo(new[] { "process-first", "process-second" }));
            });
            var firstTarget = new Dictionary<string, object?> { ["instanceId"] = first.InstanceId, ["sceneId"] = first.SceneId };
            var secondTarget = new Dictionary<string, object?> { ["instanceId"] = second.InstanceId, ["sceneId"] = second.SceneId };
            var edit = new Dictionary<string, object?>(secondTarget)
            {
                ["schemaVersion"] = "1",
                ["patch"] = new JsonObject { ["Id"] = second.SceneId, ["Name"] = "remote-process-edited" }
            };
            AssertSuccess(await client.CallToolAsync("apply_edit", edit));
            JsonObject firstDocument = Payload(await client.CallToolAsync("read_document", firstTarget));
            JsonObject secondDocument = Payload(await client.CallToolAsync("read_document", secondTarget));
            Assert.Multiple(() =>
            {
                Assert.That(firstDocument["value"]!["document"]!["Name"]!.GetValue<string>(), Is.Not.EqualTo("remote-process-edited"));
                Assert.That(secondDocument["value"]!["document"]!["Name"]!.GetValue<string>(), Is.EqualTo("remote-process-edited"));
            });
            second.Process.Kill(entireProcessTree: true);
            await second.Process.WaitForExitAsync();
            AssertError(await client.CallToolAsync("apply_edit", edit), "instance_unavailable");
            JsonArray survivors = Payload(await client.CallToolAsync("list_instances"))["value"]!["instances"]!.AsArray();
            Assert.That(survivors.Select(info => info!["instanceId"]!.GetValue<string>()), Is.EqualTo(new[] { first.InstanceId }));
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTest]
    public async Task One_connection_discovers_and_edits_two_hosts_without_shared_selection()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var firstEditor = new EditorService(TestShell.Extensions);
        var secondEditor = new EditorService(TestShell.Extensions);
        Scene firstScene = OpenScene(firstEditor, directory, "first");
        Scene secondScene = OpenScene(secondEditor, directory, "second");
        int preferredPort = AvailablePort();
        await using var first = CreateHost(firstEditor, directory, preferredPort);
        await using var second = CreateHost(secondEditor, directory, preferredPort);
        try
        {
            await first.StartAsync();
            await second.StartAsync();
            await using McpClient client = await ConnectAsync(first);
            await using McpClient otherClient = await ConnectAsync(first);
            var tools = await client.ListToolsAsync();
            Assert.Multiple(() =>
            {
                Assert.That(tools.Select(tool => tool.Name), Does.Contain("list_instances"));
                foreach (var tool in tools.Where(tool => tool.Name != "list_instances"))
                {
                    Assert.That(tool.JsonSchema.GetProperty("properties").TryGetProperty("instanceId", out _), Is.True, tool.Name);
                    if (tool.JsonSchema.TryGetProperty("required", out JsonElement required))
                        Assert.That(required.EnumerateArray().Select(item => item.GetString()), Does.Not.Contain("instanceId"), tool.Name);
                }
            });

            JsonObject discovery = Payload(await client.CallToolAsync("list_instances"));
            JsonArray instances = discovery["value"]!["instances"]!.AsArray();
            Assert.Multiple(() =>
            {
                Assert.That(discovery["value"]!["connectedInstanceId"]!.GetValue<string>(), Is.EqualTo(first.InstanceId));
                Assert.That(instances.Select(info => info!["instanceId"]!.GetValue<string>()),
                    Is.EquivalentTo(new[] { first.InstanceId, second.InstanceId }));
                Assert.That(instances.Single(info => info!["instanceId"]!.GetValue<string>() == second.InstanceId)!["sceneName"]!.GetValue<string>(), Is.EqualTo("second"));
            });

            AssertSuccess(await client.CallToolAsync("list_scenes", Target(first)));
            AssertSuccess(await client.CallToolAsync("list_scenes", Target(second)));
            await Task.WhenAll(
                RenameAsync(client, second, secondScene, "second-edited"),
                RenameAsync(otherClient, first, firstScene, "first-edited"));
            Assert.Multiple(() =>
            {
                Assert.That(firstScene.Name, Is.EqualTo("first-edited"));
                Assert.That(secondScene.Name, Is.EqualTo("second-edited"));
            });

            AssertSuccess(await client.CallToolAsync("undo", Target(second, secondScene)));
            Assert.Multiple(() =>
            {
                Assert.That(firstScene.Name, Is.EqualTo("first-edited"));
                Assert.That(secondScene.Name, Is.EqualTo("second"));
            });
            AssertSuccess(await client.CallToolAsync("redo", Target(second, secondScene)));
            JsonObject remote = Payload(await client.CallToolAsync("read_document_summary", Target(second, secondScene)));
            JsonObject local = Payload(await client.CallToolAsync("read_document_summary", Target(first, firstScene)));
            Assert.Multiple(() =>
            {
                Assert.That(remote["value"]!["rootId"]!.GetValue<string>(), Is.EqualTo(secondScene.Id.ToString()));
                Assert.That(local["value"]!["rootId"]!.GetValue<string>(), Is.EqualTo(firstScene.Id.ToString()));
            });
        }
        finally
        {
            await second.StopAsync();
            await first.StopAsync();
            await firstEditor.CloseTabItem(firstEditor.SelectedTabItem.Value!, saveChanges: false);
            await secondEditor.CloseTabItem(secondEditor.SelectedTabItem.Value!, saveChanges: false);
            Directory.Delete(directory, true);
        }

    }

    [AvaloniaTest]
    public async Task Stopped_instance_and_reused_port_never_redirect_an_edit_to_a_replacement()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        var editor = new EditorService(TestShell.Extensions);
        Scene scene = OpenScene(editor, directory, "replacement");
        await using var first = CreateHost(new EditorService(TestShell.Extensions), directory);
        await using var second = CreateHost(new EditorService(TestShell.Extensions), directory);
        try
        {
            await first.StartAsync();
            await second.StartAsync();
            await using McpClient client = await ConnectAsync(first);
            string registrationPath = Path.Combine(directory, second.InstanceId + ".json");
            string staleRegistration = File.ReadAllText(registrationPath);
            int port = second.EndpointUri!.Port;
            await second.StopAsync();
            Assert.That(File.Exists(registrationPath), Is.False);
            AssertError(await client.CallToolAsync("list_scenes", Target(second)), "instance_unavailable");

            await using var replacement = CreateHost(editor, directory, port);
            await replacement.StartAsync();
            Assert.That(replacement.EndpointUri!.Port, Is.EqualTo(port));
            File.WriteAllText(registrationPath, staleRegistration); // Simulate a crashed host's leftover entry.
            AssertSuccess(await client.CallToolAsync("list_scenes", Target(replacement)));
            var arguments = Target(second, scene);
            arguments["patch"] = new JsonObject { ["Id"] = scene.Id.ToString(), ["Name"] = "wrong-instance" };
            arguments["schemaVersion"] = "1";
            AssertError(await client.CallToolAsync("apply_edit", arguments), "instance_unavailable");
            Assert.That(scene.Name, Is.EqualTo("replacement"));
            Assert.That(File.Exists(registrationPath), Is.False);
            JsonArray instances = Payload(await client.CallToolAsync("list_instances"))["value"]!["instances"]!.AsArray();
            Assert.That(instances.Select(info => info!["instanceId"]!.GetValue<string>()),
                Is.EquivalentTo(new[] { first.InstanceId, replacement.InstanceId }));
            await replacement.StopAsync();
        }
        finally
        {
            await second.StopAsync();
            await first.StopAsync();
            await editor.CloseTabItem(editor.SelectedTabItem.Value!, saveChanges: false);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task Routing_validates_arguments_and_authenticates_instance_discovery()
    {
        await TestReset.ResetShellAsync();
        string directory = CreateDirectory();
        await using var first = CreateHost(new EditorService(TestShell.Extensions), directory);
        await using var second = CreateHost(new EditorService(TestShell.Extensions), directory);
        try
        {
            await first.StartAsync();
            await second.StartAsync();
            await using McpClient client = await ConnectAsync(first);
            AssertError(await client.CallToolAsync("read_operation_status", new Dictionary<string, object?> { ["instanceId"] = 42 }), "validation_rejected");
            AssertError(await client.CallToolAsync("read_operation_status", new Dictionary<string, object?> { ["instanceId"] = "not-an-id" }), "validation_rejected");
            var unknown = Target(second);
            unknown["unexpected"] = true;
            AssertError(await client.CallToolAsync("read_operation_status", unknown), "validation_rejected");
            AssertSuccess(await client.CallToolAsync("read_operation_status", Target(first)));
            AssertSuccess(await client.CallToolAsync("read_operation_status", Target(second)));

            using var http = new HttpClient();
            Uri infoUri = new(first.EndpointUri!, "/agent-host");
            using HttpResponseMessage rejected = await http.GetAsync(infoUri);
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
            http.DefaultRequestHeaders.Add(AgentHostInstanceRouter.InstanceHeader, second.InstanceId);
            using HttpResponseMessage wrongInstance = await http.GetAsync(infoUri);
            Assert.That(wrongInstance.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        }
        finally
        {
            await second.StopAsync();
            await first.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Registry_ignores_malformed_remote_and_reused_process_registrations()
    {
        string directory = CreateDirectory();
        var registry = new AgentHostInstanceRegistry(directory);
        string instanceId = Guid.NewGuid().ToString("N");
        try
        {
            using IDisposable lease = registry.Register(instanceId, new Uri("http://127.0.0.1:59737/mcp"));
            Assert.That(registry.Read().Select(entry => entry.InstanceId), Is.EqualTo(new[] { instanceId }));
            string validPath = Path.Combine(directory, instanceId + ".json");
            JsonObject registration = JsonNode.Parse(File.ReadAllText(validPath))!.AsObject();
            Assert.That(registration.ContainsKey("token"), Is.False);
            registration["endpointUri"] = "http://example.com/mcp";
            File.WriteAllText(validPath, registration.ToJsonString());
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{");
            Assert.That(registry.Read(), Is.Empty);
            registration["endpointUri"] = "http://127.0.0.1:59737/mcp";
            registration["processStartTime"] = 0;
            File.WriteAllText(validPath, registration.ToJsonString());
            Assert.That(registry.Read(), Is.Empty);
            Assert.That(File.Exists(validPath), Is.False);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void Pruning_and_old_leases_preserve_a_replaced_registration()
    {
        string directory = CreateDirectory();
        var registry = new AgentHostInstanceRegistry(directory);
        string instanceId = Guid.NewGuid().ToString("N");
        try
        {
            using IDisposable lease = registry.Register(instanceId, new Uri("http://127.0.0.1:59737/mcp"));
            AgentHostInstanceRegistration original = registry.Read().Single();
            AgentHostInstanceRegistration replacement = original with { EndpointUri = new Uri("http://127.0.0.1:59738/mcp") };
            string path = Path.Combine(directory, instanceId + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(replacement, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            registry.RemoveIfUnchanged(original);
            lease.Dispose();
            Assert.That(registry.Read().Single(), Is.EqualTo(replacement));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    [Platform("Linux,MacOSX")]
    public void Directory_read_permission_errors_return_no_peers_and_preserve_registrations()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix directory permissions are unavailable on Windows.");
            return;
        }
        string directory = CreateDirectory();
        var registry = new AgentHostInstanceRegistry(directory);
        UnixFileMode permissions = File.GetUnixFileMode(directory);
        try
        {
            using IDisposable lease = registry.Register(Guid.NewGuid().ToString("N"), new Uri("http://127.0.0.1:59737/mcp"));
            try
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserExecute);
                Assert.That(registry.Read(), Is.Empty);
            }
            finally { File.SetUnixFileMode(directory, permissions); }
            Assert.That(registry.Read(), Has.Count.EqualTo(1));
        }
        finally
        {
            File.SetUnixFileMode(directory, permissions);
            Directory.Delete(directory, true);
        }
    }

    private static AgentHostEndpoint CreateHost(EditorService editor, string directory, int? firstPort = null)
        => new(new ProjectService(), editor, firstPort ?? AvailablePort(), "instance-test-token", registryDirectory: directory);

    private static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-instance-tests-" + Guid.NewGuid().ToString("N"));
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

    private static async Task<McpClient> ConnectAsync(AgentHostEndpoint endpoint)
        => await ConnectAsync(endpoint.EndpointUri!);

    private static async Task<McpClient> ConnectAsync(Uri endpointUri)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpointUri,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + AgentHostInstanceTestWorker.Token }
        }));

    private static Dictionary<string, object?> Target(AgentHostEndpoint endpoint, Scene? scene = null)
    {
        var arguments = new Dictionary<string, object?> { ["instanceId"] = endpoint.InstanceId };
        if (scene is not null)
            arguments["sceneId"] = scene.Id.ToString();
        return arguments;
    }

    private static async Task RenameAsync(McpClient client, AgentHostEndpoint endpoint, Scene scene, string name)
    {
        var arguments = Target(endpoint, scene);
        arguments["patch"] = new JsonObject { ["Id"] = scene.Id.ToString(), ["Name"] = name };
        arguments["schemaVersion"] = "1";
        AssertSuccess(await client.CallToolAsync("apply_edit", arguments));
    }

    private static JsonObject Payload(CallToolResult result)
        => JsonNode.Parse(result.Content.OfType<TextContentBlock>().First().Text)!.AsObject();

    private static void AssertSuccess(CallToolResult result)
        => Assert.That(Payload(result)["isSuccess"]!.GetValue<bool>(), Is.True, Payload(result).ToJsonString());

    private static void AssertError(CallToolResult result, string code)
        => Assert.That(Payload(result)["error"]?["code"]?.GetValue<string>(), Is.EqualTo(code), Payload(result).ToJsonString());

    private sealed class Worker(Process process, JsonObject ready, Task<string> errors) : IAsyncDisposable
    {
        private readonly Task<string> _remainingOutput = process.StandardOutput.ReadToEndAsync();

        public Process Process => process;
        public string InstanceId => ready["InstanceId"]!.GetValue<string>();
        public string SceneId => ready["SceneId"]!.GetValue<string>();
        public Uri EndpointUri => new(ready["EndpointUri"]!.GetValue<string>());

        public static async Task<Worker> StartAsync(string directory, string name, int preferredPort)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in new[] { typeof(AgentHostInstanceTests).Assembly.Location,
                         "--agent-host-instance", directory, name, preferredPort.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                info.ArgumentList.Add(argument);
            Process process = Process.Start(info)!;
            Task<string> errors = process.StandardError.ReadToEndAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
                {
                    if (line.StartsWith(AgentHostInstanceTestWorker.ReadyPrefix, StringComparison.Ordinal))
                        return new Worker(process, JsonNode.Parse(line[AgentHostInstanceTestWorker.ReadyPrefix.Length..])!.AsObject(), errors);
                }
                await process.WaitForExitAsync();
                throw new InvalidOperationException($"Instance worker exited before startup (exit {process.ExitCode}): " + await errors);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                process.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("stop");
                await process.StandardInput.FlushAsync();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            string error = await errors;
            await _remainingOutput;
            if (!string.IsNullOrWhiteSpace(error)) TestContext.WriteLine(error);
            process.Dispose();
        }
    }
}
