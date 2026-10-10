using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.AgentHost;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentEditFollowerTests
{
    [AvaloniaTest]
    public async Task An_agent_edit_selects_the_element_seeks_into_it_and_reveals_the_property()
    {
        Fixture fixture = await CreateFixtureAsync(follow: true);
        try
        {
            await using AgentHostEndpoint host = fixture.CreateHost();
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            Success(await client.CallToolAsync("apply_edit", Patch(fixture, new JsonObject { ["Width"] = 200 })));
            HeadlessTestHelpers.Render(2);

            ElementView farView = fixture.Window.GetVisualDescendants().OfType<ElementView>()
                .Single(view => ((ElementViewModel)view.DataContext!).Model == fixture.Far);
            ElementPropertyTabViewModel properties = fixture.Editor.FindToolTab<ElementPropertyTabViewModel>()!;
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Selection.Value, Is.SameAs(fixture.Far));
                Assert.That(fixture.Clock.Value, Is.EqualTo(fixture.Far.Start), "The playhead moves the least to show the element.");
                Assert.That(properties.Items.Single()!.Model, Is.SameAs(fixture.FarRect));
                Assert.That(HasFlash(farView.border), Is.True, "The timeline clip flashes.");
                Assert.That(Flashed(fixture.Window).Any(target => target.FindAncestorOfType<ElementPropertyTabView>() is not null
                                                                  && target.FindAncestorOfType<EngineObjectPropertyView>() is not null),
                    Is.True, "A property row flashes.");
            });

            HeadlessTestHelpers.Render(60);
            TimelineOptions options = fixture.Timeline.Options.Value;
            Assert.Multiple(() =>
            {
                Assert.That(options.Offset.X, Is.GreaterThan(0), "The timeline scrolls to the element.");
                Assert.That(options.Offset.Y, Is.GreaterThan(0), "The timeline scrolls to the element's layer.");
            });
        }
        finally
        {
            fixture.Window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Editing_a_keyframe_seeks_to_it_and_undo_returns_to_the_edit()
    {
        Fixture fixture = await CreateFixtureAsync(follow: true);
        try
        {
            var animation = new KeyFrameAnimation<float>();
            var first = new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 };
            var second = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1.5), Value = 100 };
            animation.KeyFrames.Add(first, out _);
            animation.KeyFrames.Add(second, out _);
            fixture.FarRect.Opacity.Animation = animation;
            await using AgentHostEndpoint host = fixture.CreateHost();
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            var keyFrames = new JsonArray(new JsonObject { ["Id"] = second.Id.ToString(), ["Value"] = 40 });
            Success(await client.CallToolAsync("apply_edit", Patch(fixture, new JsonObject
            {
                ["Animations"] = new JsonObject { ["Opacity"] = new JsonObject { ["KeyFrames"] = keyFrames } }
            })));
            HeadlessTestHelpers.Render(2);

            Assert.Multiple(() =>
            {
                Assert.That(second.Value, Is.EqualTo(40));
                Assert.That(fixture.Selection.Value, Is.SameAs(fixture.Far));
                Assert.That(fixture.Clock.Value, Is.EqualTo(fixture.Far.Start + second.KeyTime));
            });

            fixture.Selection.Value = fixture.Near;
            fixture.Clock.Value = TimeSpan.Zero;
            Success(await client.CallToolAsync("undo", Target(fixture.Editor.Scene.Id)));
            HeadlessTestHelpers.Render(2);

            Assert.Multiple(() =>
            {
                Assert.That(second.Value, Is.EqualTo(100));
                Assert.That(fixture.Selection.Value, Is.SameAs(fixture.Far));
                Assert.That(fixture.Clock.Value, Is.EqualTo(fixture.Far.Start + second.KeyTime));
            });
        }
        finally
        {
            fixture.Window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Render_still_moves_the_playhead_to_the_rendered_frame()
    {
        Fixture fixture = await CreateFixtureAsync(follow: true);
        try
        {
            await using AgentHostEndpoint host = fixture.CreateHost();
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            Dictionary<string, object?> arguments = Target(fixture.Editor.Scene.Id);
            arguments["outputPath"] = Path.Combine(fixture.Directory, "frame.png");
            arguments["timeSeconds"] = 13.25;
            await client.CallToolAsync("render_still", arguments);
            HeadlessTestHelpers.Render(60);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Clock.Value, Is.EqualTo(TimeSpan.FromSeconds(13.25).RoundToRate(30)));
                Assert.That(fixture.Timeline.Options.Value.Offset.X, Is.GreaterThan(0));
            });
        }
        finally
        {
            fixture.Window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Nothing_moves_while_following_is_off()
    {
        Fixture fixture = await CreateFixtureAsync(follow: false);
        try
        {
            await using AgentHostEndpoint host = fixture.CreateHost();
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            Success(await client.CallToolAsync("apply_edit", Patch(fixture, new JsonObject { ["Width"] = 200 })));
            Dictionary<string, object?> arguments = Target(fixture.Editor.Scene.Id);
            arguments["outputPath"] = Path.Combine(fixture.Directory, "frame.png");
            arguments["timeSeconds"] = 13.25;
            await client.CallToolAsync("render_still", arguments);
            HeadlessTestHelpers.Render(60);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.FarRect.Width.CurrentValue, Is.EqualTo(200));
                Assert.That(fixture.Selection.Value, Is.Null);
                Assert.That(fixture.Clock.Value, Is.EqualTo(TimeSpan.Zero));
                Assert.That(fixture.Timeline.Options.Value.Offset.X, Is.Zero);
                Assert.That(Flashed(fixture.Window), Is.Empty);
            });
        }
        finally
        {
            fixture.Window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private sealed record Fixture(
        EditViewModel Editor,
        Window Window,
        Element Near,
        Element Far,
        RectShape FarRect,
        string Directory,
        AiAgentConfig Config)
    {
        public IReactiveProperty<CoreObject?> Selection
            => Editor.GetRequiredService<IEditorSelection>().SelectedObject;

        public IReactiveProperty<TimeSpan> Clock => Editor.GetRequiredService<IEditorClock>().CurrentTime;

        public TimelineTabViewModel Timeline => Editor.FindToolTab<TimelineTabViewModel>()!;

        public AgentHostEndpoint CreateHost() => new(TestShell.Project, TestShell.Editor, Config);
    }

    private static async Task<Fixture> CreateFixtureAsync(bool follow)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "follow-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(640, 360, 30, 44100, "Follow", directory))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        scene.Duration = TimeSpan.FromSeconds(20);
        Element near = AddRectangle(scene, directory, "near", TimeSpan.Zero, 0);
        // Far right and far down, so the timeline has to scroll both ways to show it.
        Element far = AddRectangle(scene, directory, "far", TimeSpan.FromSeconds(12), 20);
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1280, Height = 800 };
        window.Show();
        HeadlessTestHelpers.Render(5);
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = null;
        editor.GetRequiredService<IEditorClock>().CurrentTime.Value = TimeSpan.Zero;
        HeadlessTestHelpers.Render(2);

        var config = new AiAgentConfig
        {
            WorkspaceRoot = BeutlHomeIsolation.CurrentHome!,
            FollowLiveMcpEdits = follow
        };
        return new Fixture(editor, window, near, far, (RectShape)far.Objects.Single(), directory, config);
    }

    private static Element AddRectangle(Scene scene, string directory, string name, TimeSpan start, int zIndex)
    {
        var element = new Element
        {
            Name = name,
            Start = start,
            Length = TimeSpan.FromSeconds(2),
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(directory, $"{name}.belm"))
        };
        element.AddObject(new RectShape { Name = name, Width = { CurrentValue = 100 }, Height = { CurrentValue = 100 } });
        scene.Children.Add(element);
        return element;
    }

    private static Dictionary<string, object?> Patch(Fixture fixture, JsonObject rect)
    {
        rect["Id"] = fixture.FarRect.Id.ToString();
        Dictionary<string, object?> arguments = Target(fixture.Editor.Scene.Id);
        arguments["schemaVersion"] = "1";
        arguments["patch"] = new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = fixture.Far.Id.ToString(),
                ["Objects"] = new JsonArray(rect)
            })
        };
        return arguments;
    }

    private static IEnumerable<Control> Flashed(Window window)
        => window.GetVisualDescendants().OfType<AdornerLayer>()
            .SelectMany(layer => layer.Children)
            .Select(adorner => AdornerLayer.GetAdornedElement(adorner))
            .OfType<Control>();

    private static bool HasFlash(Control target)
        => Flashed(target.FindAncestorOfType<Window>()!).Contains(target);

    private static async Task<McpClient> ConnectAsync(AgentHostEndpoint host)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = host.EndpointUri!,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token }
        }));

    private static Dictionary<string, object?> Target(Guid sceneId)
        => new() { ["sceneId"] = sceneId.ToString() };

    private static JsonObject Success(CallToolResult result)
    {
        JsonObject payload = JsonNode.Parse(result.Content.OfType<TextContentBlock>().First().Text)!.AsObject();
        Assert.That(payload["isSuccess"]!.GetValue<bool>(), Is.True, payload.ToJsonString());
        return payload["value"]!.AsObject();
    }
}
