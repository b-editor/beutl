using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentHostSceneRoutingTests
{
    [AvaloniaTest]
    public async Task Scene_calls_require_a_target_and_preserve_the_visible_editor()
    {
        (Scene first, Scene second, _) = await CreateScenesAsync();
        if (TestShell.Editor.TryGetTabItem(second, out var secondTab))
            await TestShell.Editor.CloseTabItem(secondTab, saveChanges: false);
        var visibleTab = TestShell.Editor.SelectedTabItem.Value;
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);

        var tools = await client.ListToolsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain("attach_active_editor"));
            foreach (string name in new[] { "apply_edit", "read_document", "undo", "render_still", "add_scene", "save_project" })
            {
                JsonElement schema = tools.Single(tool => tool.Name == name).JsonSchema;
                Assert.That(schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                    Does.Contain("sceneId").And.Not.Contain("session"), name);
                Assert.That(schema.GetProperty("properties").TryGetProperty("session", out _), Is.False, name);
            }
        });

        JsonObject listing = Success(await client.CallToolAsync("list_scenes"));
        Assert.Multiple(() =>
        {
            Assert.That(listing["scenes"]!.AsArray().Select(scene => scene!["sceneId"]!.GetValue<string>()),
                Is.EquivalentTo(new[] { first.Id.ToString(), second.Id.ToString() }));
            Assert.That(listing["activeSceneId"]!.GetValue<string>(), Is.EqualTo(first.Id.ToString()));
            Assert.That(TestShell.Editor.TryGetTabItem(second, out _), Is.False);
        });

        var missingTarget = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "1",
            ["patch"] = new JsonObject { ["Name"] = "wrong" }
        };
        Error(await client.CallToolAsync("apply_edit", missingTarget), ErrorCode.ValidationRejected);
        Error(await client.CallToolAsync("read_document", Target(Guid.NewGuid())), ErrorCode.StaleHandle);
        var unknown = Target(second.Id);
        unknown["unexpected"] = true;
        Error(await client.CallToolAsync("read_document", unknown), ErrorCode.ValidationRejected);
        var legacy = Target(second.Id);
        legacy["session"] = first.Id.ToString();
        Error(await client.CallToolAsync("save_project", legacy), ErrorCode.ValidationRejected);
        Assert.That(SelectedScene(), Is.SameAs(first), "Rejected calls must not activate another tab.");

        Success(await client.CallToolAsync("read_document", Target(second.Id)));
        Assert.Multiple(() =>
        {
            Assert.That(SelectedScene(), Is.SameAs(first));
            Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(visibleTab));
            Assert.That(TestShell.Editor.TryGetTabItem(second, out var backgroundTab), Is.True);
            Assert.That(backgroundTab!.IsSelected.Value, Is.False);
        });
        Success(await client.CallToolAsync("read_document_summary", Target(first.Id)));
        Assert.That(SelectedScene(), Is.SameAs(first));
        JsonObject status = Success(await client.CallToolAsync("read_operation_status"));
        Assert.That(status["hasActiveSession"]!.GetValue<bool>(), Is.False,
            "A new request must not inherit a previous call's scene binding.");
    }

    [AvaloniaTest]
    public async Task Scene_calls_reject_targets_while_project_tabs_are_disposing()
    {
        (Scene first, Scene second, string directory) = await CreateScenesAsync();
        if (TestShell.Editor.TryGetTabItem(second, out var secondTab))
            await TestShell.Editor.CloseTabItem(secondTab, saveChanges: false);
        Project project = TestShell.Project.CurrentProject.Value!;
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);

        var disposalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var enabled = new ReactivePropertySlim<bool>(true);
        var context = new Mock<IEditorContext>();
        context.SetupGet(editor => editor.Object).Returns(new Scene
        {
            Uri = new Uri(Path.Combine(directory, "disposing.scene"))
        });
        context.SetupGet(editor => editor.Extension).Returns(SceneEditorExtension.Instance);
        context.SetupGet(editor => editor.IsEnabled).Returns(enabled);
        context.Setup(editor => editor.DisposeAsync()).Returns(async () =>
        {
            disposalStarted.TrySetResult();
            await releaseDisposal.Task;
        });
        TestShell.Editor.TabItems.Insert(0, new EditorTabItem(context.Object));

        Task closing = TestShell.Project.CloseProjectAsync();
        try
        {
            await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Project.CurrentTransition, Is.Not.Null);
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
                Assert.That(TestShell.Editor.TabItems, Is.Empty);
            });

            Error(await client.CallToolAsync("read_document", Target(second.Id)), ErrorCode.NoActiveEditorSession);
            var arguments = Target(first.Id);
            arguments["schemaVersion"] = "1";
            arguments["patch"] = new JsonObject { ["Name"] = "Edit during close" };
            Error(await client.CallToolAsync("apply_edit", arguments), ErrorCode.NoActiveEditorSession);
            Assert.That(TestShell.Editor.TabItems, Is.Empty, "Rejected calls must not escape the close snapshot.");
        }
        finally
        {
            releaseDisposal.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value, Is.Null);
            Assert.That(TestShell.Editor.TabItems, Is.Empty);
            Assert.That(first.Name, Is.EqualTo("First"));
        });
    }

    [AvaloniaTest]
    public async Task Background_scene_binding_rejects_calls_during_a_worktree_mutation()
    {
        (Scene first, Scene second, _) = await CreateScenesAsync();
        if (TestShell.Editor.TryGetTabItem(second, out var secondTab))
            await TestShell.Editor.CloseTabItem(secondTab, saveChanges: false);
        var visibleTab = TestShell.Editor.SelectedTabItem.Value;
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);

        using (TestShell.Editor.SuspendEditors())
        using (IDisposable? mutation = TestShell.Editor.TryBeginWorktreeMutation())
        {
            Assert.That(mutation, Is.Not.Null);
            Assert.That(TestShell.Project.CurrentTransition, Is.Null,
                "Exercise editor-open admission independently of the project-transition check.");
            Error(await client.CallToolAsync("read_document", Target(second.Id)), ErrorCode.NoActiveEditorSession);
            var arguments = Target(second.Id);
            arguments["schemaVersion"] = "1";
            arguments["patch"] = new JsonObject { ["Name"] = "Lost edit" };
            Error(await client.CallToolAsync("apply_edit", arguments), ErrorCode.NoActiveEditorSession);
            Assert.Multiple(() =>
            {
                Assert.That(TestShell.Editor.TryGetTabItem(second, out _), Is.False);
                Assert.That(second.Name, Is.EqualTo("Second"));
                Assert.That(TestShell.Editor.SelectedTabItem.Value, Is.SameAs(visibleTab));
            });
        }

        await RenameAsync(client, second, "Second edited");
        Assert.Multiple(() =>
        {
            Assert.That(second.Name, Is.EqualTo("Second edited"));
            Assert.That(SelectedScene(), Is.SameAs(first));
        });
    }

    [AvaloniaTest]
    [TestCase(42)]
    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_scene_targets_do_not_fall_back_to_the_visible_scene(object sceneId)
    {
        (Scene first, _, _) = await CreateScenesAsync();
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        var arguments = new Dictionary<string, object?>
        {
            ["sceneId"] = sceneId,
            ["schemaVersion"] = "1",
            ["patch"] = new JsonObject { ["Name"] = "Wrong target" }
        };
        Error(await client.CallToolAsync("apply_edit", arguments), ErrorCode.ValidationRejected);
        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("First"));
            Assert.That(SelectedScene(), Is.SameAs(first));
        });
    }

    [AvaloniaTest]
    public async Task Concurrent_clients_keep_scene_edits_and_history_independent()
    {
        (Scene first, Scene second, _) = await CreateScenesAsync();
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        await using McpClient otherClient = await ConnectAsync(host);

        await Task.WhenAll(RenameAsync(client, first, "First edited"), RenameAsync(otherClient, second, "Second edited"));
        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("First edited"));
            Assert.That(second.Name, Is.EqualTo("Second edited"));
            Assert.That(SelectedScene(), Is.SameAs(first));
        });

        TestShell.Editor.ActivateTabItem(first);
        Success(await otherClient.CallToolAsync("undo", Target(second.Id)));
        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("First edited"));
            Assert.That(second.Name, Is.EqualTo("Second"));
            Assert.That(SelectedScene(), Is.SameAs(first));
        });
        Success(await client.CallToolAsync("redo", Target(second.Id)));
        Success(await otherClient.CallToolAsync("read_history", Target(first.Id)));
        Assert.Multiple(() =>
        {
            Assert.That(second.Name, Is.EqualTo("Second edited"));
            Assert.That(SelectedScene(), Is.SameAs(first));
        });

        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        editor.IsEnabled.Value = false;
        try
        {
            var arguments = Target(first.Id);
            arguments["schemaVersion"] = "1";
            arguments["patch"] = new JsonObject { ["Name"] = "Disabled edit" };
            Error(await client.CallToolAsync("apply_edit", arguments), ErrorCode.NoActiveEditorSession);
            Assert.That(first.Name, Is.EqualTo("First edited"));
        }
        finally { editor.IsEnabled.Value = true; }
    }

    [AvaloniaTest]
    public async Task Project_lifecycle_and_composition_plans_do_not_select_later_request_targets()
    {
        await TestReset.ResetShellAsync();
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "stateless-" + Guid.NewGuid().ToString("N"), "project.bep");
        JsonObject created = Success(await client.CallToolAsync("create_project", new Dictionary<string, object?>
        {
            ["path"] = path,
            ["width"] = 640,
            ["height"] = 360,
            ["frameRate"] = 30,
            ["duration"] = "00:00:05"
        }));
        string firstId = created["summary"]!["scenes"]![0]!["sceneId"]!.GetValue<string>();
        Scene firstScene = TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(firstScene);
        var arguments = Target(Guid.Parse(firstId));
        arguments["width"] = 640;
        arguments["height"] = 360;
        arguments["start"] = "00:00:00";
        arguments["duration"] = "00:00:05";
        arguments["name"] = "Second";
        JsonObject added = Success(await client.CallToolAsync("add_scene", arguments));
        Guid secondId = Guid.Parse(added["sceneId"]!.GetValue<string>());
        Assert.That(SelectedScene(), Is.SameAs(firstScene));
        Error(await client.CallToolAsync("read_document"), ErrorCode.ValidationRejected);

        Success(await client.CallToolAsync("open_project", new Dictionary<string, object?> { ["path"] = path }));
        Assert.That(SelectedScene().Id.ToString(), Is.EqualTo(firstId));
        var planArguments = Target(secondId);
        planArguments["name"] = "kinetic-ribbon-title";
        JsonObject plan = Success(await client.CallToolAsync("plan_composition", planArguments));
        string planId = plan["planId"]!.GetValue<string>();
        var wrongScene = Target(Guid.Parse(firstId));
        wrongScene["planId"] = planId;
        Error(await client.CallToolAsync("apply_composition", wrongScene), ErrorCode.StaleHandle);
        var applyArguments = Target(secondId);
        applyArguments["planId"] = planId;
        Success(await client.CallToolAsync("apply_composition", applyArguments));
        Assert.Multiple(() =>
        {
            Assert.That(SelectedScene(), Is.SameAs(firstScene));
            Assert.That(TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>()
                .Single(scene => scene.Id == secondId).Children, Is.Not.Empty);
            Assert.That(firstScene.Children, Is.Empty);
        });
        JsonObject save = Success(await client.CallToolAsync("save_project", Target(secondId)));
        Assert.That(save["saved"]!.GetValue<bool>(), Is.False, "Live persistence remains the editor's responsibility.");
    }

    [AvaloniaTest]
    public async Task Rendering_uses_the_requested_scene_without_changing_the_visible_scene()
    {
        (Scene first, Scene second, string directory) = await CreateScenesAsync();
        AddRectangle(first, Brushes.Red);
        AddRectangle(second, Brushes.Lime);
        await using var host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        string output = Path.Combine(directory, "second.png");
        var arguments = Target(second.Id);
        arguments["outputPath"] = output;
        arguments["returnImageContent"] = false;
        JsonObject rendered = Payload(await client.CallToolAsync("render_still", arguments));
        if (rendered["error"]?["code"]?.GetValue<string>() == ErrorCode.RenderingUnavailable)
            Assert.Ignore(rendered.ToJsonString());
        Assert.That(rendered["isSuccess"]!.GetValue<bool>(), Is.True, rendered.ToJsonString());
        Assert.That(SelectedScene(), Is.SameAs(first));
        using SKBitmap bitmap = SKBitmap.Decode(output);
        Assert.That(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2), Is.EqualTo(SKColors.Lime));

        if (Environment.GetEnvironmentVariable("BEUTL_SCENE_CAPTURE_PATH") is { Length: > 0 } capturePath)
        {
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            var window = new Window { Content = new EditView { DataContext = editor }, Width = 1280, Height = 800 };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render(5);
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(capturePath, PngBitmapEncoderOptions.Default);
                TestContext.Out.WriteLine($"Scene UI capture: {capturePath}");
            }
            finally { window.Close(); HeadlessTestHelpers.Settle(); }
        }
    }

    private static async Task<(Scene First, Scene Second, string Directory)> CreateScenesAsync()
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "scene-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(640, 360, 30, 44100, "First", directory))!;
        Scene first = project.Items.OfType<Scene>().Single();
        first.Name = "First";
        Scene second = ProjectOperations.AddScene(project, new SceneCreateOptions(
            640, 360, TimeSpan.Zero, TimeSpan.FromSeconds(5), "Second"));
        ProjectOperations.Save(project);
        TestShell.Editor.ActivateTabItem(first);
        HeadlessTestHelpers.Settle();
        return (first, second, directory);
    }

    private static void AddRectangle(Scene scene, Brush fill)
    {
        var element = new Element { Name = scene.Name + " rectangle", Length = scene.Duration };
        element.AddObject(new RectShape
        {
            Width = { CurrentValue = 640 },
            Height = { CurrentValue = 360 },
            Fill = { CurrentValue = fill }
        });
        scene.Children.Add(element);
    }

    private static Scene SelectedScene()
        => ((EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value).Scene;

    private static AgentHostEndpoint CreateHost()
        => new(TestShell.Project, TestShell.Editor, new AiAgentConfig
        {
            WorkspaceRoot = BeutlHomeIsolation.CurrentHome!,
            LiveMcpToken = "scene-routing-test-token"
        });

    private static async Task<McpClient> ConnectAsync(AgentHostEndpoint host)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = host.EndpointUri!,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token }
        }));

    private static Dictionary<string, object?> Target(Guid sceneId)
        => new() { ["sceneId"] = sceneId.ToString() };

    private static async Task RenameAsync(McpClient client, Scene scene, string name)
    {
        var arguments = Target(scene.Id);
        arguments["schemaVersion"] = "1";
        arguments["patch"] = new JsonObject { ["Name"] = name };
        Success(await client.CallToolAsync("apply_edit", arguments));
    }

    private static JsonObject Payload(CallToolResult result)
        => JsonNode.Parse(result.Content.OfType<TextContentBlock>().First().Text)!.AsObject();

    private static JsonObject Success(CallToolResult result)
    {
        JsonObject payload = Payload(result);
        Assert.That(payload["isSuccess"]!.GetValue<bool>(), Is.True, payload.ToJsonString());
        return payload["value"]!.AsObject();
    }

    private static void Error(CallToolResult result, string code)
    {
        JsonObject payload = Payload(result);
        Assert.That(payload["error"]?["code"]?.GetValue<string>(), Is.EqualTo(code), payload.ToJsonString());
    }
}
