using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class SceneNodeTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-scene-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Test]
    public void DrawsTheSceneAtItsTimeInputWhateverTheGraphsTime()
    {
        Scene referenced = CreateScene(new RectShape());
        var (model, node) = CreateGraph(referenced);

        node.Time.Property!.SetValue(TimeSpan.FromSeconds(0.5));
        Assert.That(Evaluate(model, node, TimeSpan.FromSeconds(2)), Is.EqualTo(1), "The element is on screen at 0.5 s.");

        node.Time.Property!.SetValue(TimeSpan.FromSeconds(2));
        Assert.That(Evaluate(model, node, TimeSpan.FromSeconds(0.5)), Is.EqualTo(0), "The element has ended at 2 s.");
        Assert.That(node.ErrorMonitor.Value, Is.Null);
    }

    [Test]
    public void ATimeNodePlaysTheSceneInStepWithTheGraph()
    {
        Scene referenced = CreateScene(new RectShape());
        var (model, node) = CreateGraph(referenced);
        var time = new Beutl.NodeGraph.Nodes.Utilities.TimeNode();
        model.Nodes.Add(time);
        Connection connection = model.Connect(node.Time, time.Time);

        Assert.That(Evaluate(model, node, TimeSpan.FromSeconds(0.5)), Is.EqualTo(1), "Seconds reach the time input.");
        Assert.That(Evaluate(model, node, TimeSpan.FromSeconds(2)), Is.EqualTo(0));
        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Convert));
    }

    [Test]
    public void OutputActuallyDrawsTheReferencedScene()
    {
        var rect = new RectShape
        {
            Width = { CurrentValue = 20 },
            Height = { CurrentValue = 20 },
            AlignmentX = { CurrentValue = Beutl.Media.AlignmentX.Left },
            AlignmentY = { CurrentValue = Beutl.Media.AlignmentY.Top },
            Fill = { CurrentValue = new Beutl.Media.SolidColorBrush(Beutl.Media.Colors.Red) },
        };
        Scene referenced = CreateScene(rect);
        var (model, node) = CreateGraph(referenced);
        node.Time.Property!.SetValue(TimeSpan.FromSeconds(0.5));

        using var snapshot = new GraphSnapshot();
        var context = new CompositionContext(TimeSpan.FromSeconds(3));
        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);
        var resource = (SceneNode.Resource)snapshot.GetResource(snapshot.FindSlotIndex(node))!;
        Assert.That(resource.Output, Is.Not.Null);

        using var renderer = new RenderNodeRenderer(resource.Output!, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = new Beutl.Graphics.Rect(0, 0, 64, 48),
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        Assert.That(renderer.Measure().OutputBounds, Is.EqualTo(new Beutl.Graphics.Rect(0, 0, 20, 20)),
            "The referenced scene's content is drawn, not just evaluated.");
    }

    [Test]
    public void TimeNamesTheReferencedScenesTimeWhereverTheGraphsElementStarts()
    {
        Scene referenced = CreateScene(new RectShape());
        var (model, node) = CreateGraph(referenced);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = model;
        var host = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(10),
            Uri = new Uri(Path.Combine(_directory, "host.layer")),
        };
        host.AddObject(drawable);
        // As in the editor: the graph's element sits in a scene of its own.
        var outer = new Scene(64, 48, "outer") { Uri = new Uri(Path.Combine(_directory, "outer.scene")) };
        outer.Children.Add(host);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(outer);
        project.Items.Add(referenced);
        Assert.That(node.Object.Start, Is.EqualTo(TimeSpan.Zero), "The scene's time does not follow the hosting element.");

        node.Time.Property!.SetValue(TimeSpan.FromSeconds(0.5));

        Assert.That(Evaluate(model, node, TimeSpan.FromSeconds(6)), Is.EqualTo(1),
            "0.5 s of the referenced scene is drawn, not 0.5 s minus the element's start.");
    }

    // The whole editor path: an outer scene, an element starting at 5 s, a node graph whose
    // output node draws the Scene node, and the referenced scene's red square.
    [TestCase(0.5, true)]
    [TestCase(2.0, false)]
    public void TheEditorsRenderShowsTheReferencedSceneAtTheTimeGiven(double seconds, bool expectRed)
    {
        Beutl.UnitTests.Engine.Graphics.Backend.VulkanTestEnvironment.EnsureAvailable();
        Beutl.UnitTests.Engine.Graphics.Backend.VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var rect = new RectShape
            {
                Width = { CurrentValue = 20 },
                Height = { CurrentValue = 20 },
                AlignmentX = { CurrentValue = Beutl.Media.AlignmentX.Left },
                AlignmentY = { CurrentValue = Beutl.Media.AlignmentY.Top },
                Fill = { CurrentValue = new Beutl.Media.SolidColorBrush(Beutl.Media.Colors.Red) },
            };
            Scene referenced = CreateScene(rect);
            var (model, node) = CreateGraph(referenced);
            node.Time.Property!.SetValue(TimeSpan.FromSeconds(seconds));
            var output = new OutputNode();
            model.Nodes.Add(output);
            model.Connect(output.InputPort, node.Output);
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = model;
            var host = new Element
            {
                Start = TimeSpan.FromSeconds(5),
                Length = TimeSpan.FromSeconds(10),
                Uri = new Uri(Path.Combine(_directory, "host.layer")),
            };
            host.AddObject(drawable);
            var outer = new Scene(64, 48, "outer") { Uri = new Uri(Path.Combine(_directory, "outer.scene")) };
            outer.Children.Add(host);
            var application = new BeutlApplication();
            var project = new Project();
            application.Project = project;
            project.Items.Add(outer);
            project.Items.Add(referenced);

            using var renderer = new SceneRenderer(outer, Beutl.Graphics.Rendering.RenderIntent.Preview);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(6)));
            using Beutl.Media.Bitmap snapshot = renderer.Snapshot();
            using Beutl.Media.Bitmap srgb = snapshot.Convert(
                Beutl.Media.BitmapColorType.Bgra8888, Beutl.Media.BitmapAlphaType.Unpremul, Beutl.Media.BitmapColorSpace.Srgb);
            int red = 0;
            for (int y = 0; y < srgb.Height; y++)
            {
                ReadOnlySpan<Beutl.Media.Pixel.Bgra8888> row = srgb.GetRow<Beutl.Media.Pixel.Bgra8888>(y);
                for (int x = 0; x < srgb.Width; x++)
                {
                    if (row[x].R > 200 && row[x].G < 60 && row[x].B < 60) red++;
                }
            }

            Assert.That(red > 0, Is.EqualTo(expectRed), $"red pixels: {red}; error: {node.ErrorMonitor.Value}");
        });
    }

    [Test]
    public void TimeStaysAbsoluteAfterSavingAndLoading()
    {
        var model = new GraphModel();
        model.Nodes.Add(new SceneNode());
        var json = Beutl.Serialization.CoreSerializer.SerializeToJsonObject(model);
        // A graph saved before the node anchored its time carries no time range of its own.
        var saved = (System.Text.Json.Nodes.JsonObject)json["Nodes"]![0]!["Object"]!;
        saved.Remove("Start");
        saved.Remove("Duration");
        saved.Remove("ZIndex");
        var restored = (GraphModel)Beutl.Serialization.CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        SceneNode node = restored.Nodes.OfType<SceneNode>().Single();

        Assert.That(node.Object.IsTimeAnchor, Is.True);
        Assert.That(node.Object.Start, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ASceneThatContainsTheGraphDoesNotStopTheRender()
    {
        var graph = new GraphModel();
        var node = new SceneNode();
        graph.Nodes.Add(node);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = graph;
        Scene scene = CreateScene(drawable);
        node.Object.ReferencedScene.CurrentValue = scene;

        using var compositor = new SceneCompositor(scene);
        Assert.DoesNotThrow(() => compositor.EvaluateGraphics(TimeSpan.FromSeconds(0.5)));
        Assert.DoesNotThrow(() => Evaluate(graph, node, TimeSpan.FromSeconds(0.5)));
    }

    private Scene CreateScene(Beutl.Engine.EngineObject content)
    {
        var scene = new Scene(64, 48, "referenced") { Uri = new Uri(Path.Combine(_directory, $"{Guid.NewGuid():N}.scene")) };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(_directory, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(content);
        scene.Children.Add(element);
        return scene;
    }

    private static (GraphModel Model, SceneNode Node) CreateGraph(Scene referenced)
    {
        var model = new GraphModel();
        var node = new SceneNode();
        node.Object.ReferencedScene.CurrentValue = referenced;
        model.Nodes.Add(node);
        return (model, node);
    }

    // How many objects of the referenced scene were drawn.
    private static int Evaluate(GraphModel model, SceneNode node, TimeSpan time)
    {
        using var snapshot = new GraphSnapshot();
        var context = new CompositionContext(time);
        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);
        var resource = (SceneNode.Resource)snapshot.GetResource(snapshot.FindSlotIndex(node))!;
        return resource.SceneResource?.Frame?.Objects.Length ?? -1;
    }
}
