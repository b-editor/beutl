using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

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
        var (model, node) = CreateGraph();
        HostGraph(model, node, referenced, startSeconds: 0);

        node.Time.Property!.SetValue(MediaTime.FromSeconds(0.5));
        Assert.That(Evaluate(model, node, MediaTime.FromSeconds(2)), Is.EqualTo(1), "The element is on screen at 0.5 s.");

        node.Time.Property!.SetValue(MediaTime.FromSeconds(2));
        Assert.That(Evaluate(model, node, MediaTime.FromSeconds(0.5)), Is.EqualTo(0), "The element has ended at 2 s.");
        Assert.That(node.ErrorMonitor.Value, Is.Null);
    }

    [Test]
    public void ATimeNodePlaysTheSceneInStepWithTheGraph()
    {
        Scene referenced = CreateScene(new RectShape());
        var (model, node) = CreateGraph();
        HostGraph(model, node, referenced, startSeconds: 0);
        var time = new Beutl.NodeGraph.Nodes.Utilities.TimeNode();
        model.Nodes.Add(time);
        Connection connection = model.Connect(node.Time, time.Time);

        Assert.That(Evaluate(model, node, MediaTime.FromSeconds(0.5)), Is.EqualTo(1), "Seconds reach the time input.");
        Assert.That(Evaluate(model, node, MediaTime.FromSeconds(2)), Is.EqualTo(0));
        Assert.That(connection.Status.Value, Is.EqualTo(ConnectionStatus.Convert));
    }

    [Test]
    public void OutputActuallyDrawsTheReferencedScene()
    {
        Scene referenced = CreateScene(CreateRedSquare());
        var (model, node) = CreateGraph();
        HostGraph(model, node, referenced, startSeconds: 0);
        node.Time.Property!.SetValue(MediaTime.FromSeconds(0.5));

        using var snapshot = new GraphSnapshot();
        var context = new CompositionContext(MediaTime.FromSeconds(3));
        snapshot.Build(model, context);
        snapshot.Evaluate(context);
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
        var (model, node) = CreateGraph();
        HostGraph(model, node, referenced, startSeconds: 5);
        Assert.That(node.Object.Value.Start, Is.EqualTo(MediaTime.Zero), "The scene's time does not follow the hosting element.");

        node.Time.Property!.SetValue(MediaTime.FromSeconds(0.5));

        Assert.That(Evaluate(model, node, MediaTime.FromSeconds(6)), Is.EqualTo(1),
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
            Scene referenced = CreateScene(CreateRedSquare());
            var (model, node) = CreateGraph();
            node.Time.Property!.SetValue(MediaTime.FromSeconds(seconds));
            var output = new OutputNode();
            model.Nodes.Add(output);
            model.Connect(output.InputPort, node.Output);
            Scene outer = HostGraph(model, node, referenced, startSeconds: 5);

            using var renderer = new SceneRenderer(outer, Beutl.Graphics.Rendering.RenderIntent.Preview);
            renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(6)));
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
        var restored = (GraphModel)Beutl.Serialization.CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        SceneNode node = restored.Nodes.OfType<SceneNode>().Single();

        Assert.That(node.Object.Value.IsTimeAnchor.Value, Is.True);
        Assert.That(node.Object.Value.Start, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void ASceneThatContainsTheGraphDoesNotStopTheRender()
    {
        var graph = new GraphModel();
        var node = new SceneNode();
        graph.Nodes.Add(node);
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value = graph;
        Scene scene = CreateScene(drawable);
        node.Object.Value.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, scene);

        using var compositor = new SceneCompositor(scene);
        Assert.DoesNotThrow(() => compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)));
        Assert.DoesNotThrow(() => Evaluate(graph, node, MediaTime.FromSeconds(0.5)));
    }

    private Scene CreateScene(Beutl.Engine.EngineObject content)
    {
        var scene = new Scene(64, 48, "referenced") { Uri = new Uri(Path.Combine(_directory, $"{Guid.NewGuid():N}.scene")) };
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(_directory, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(content);
        scene.Children.Add(element);
        return scene;
    }

    // A 20 px red square at the top left.
    private static RectShape CreateRedSquare() => new()
    {
        Width = { Value = 20 },
        Height = { Value = 20 },
        AlignmentX = { Value = Beutl.Media.AlignmentX.Left },
        AlignmentY = { Value = Beutl.Media.AlignmentY.Top },
        Fill = { Value = new Beutl.Media.SolidColorBrush(Beutl.Media.Colors.Red) },
    };

    // The graph and referenced scene share a project, with an explicit element time anchor.
    private Scene HostGraph(GraphModel model, SceneNode node, Scene referenced, double startSeconds)
    {
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value = model;
        var host = new Element
        {
            Start = { Value = MediaTime.FromSeconds(startSeconds) },
            Length = { Value = MediaTime.FromSeconds(10) },
            Uri = new Uri(Path.Combine(_directory, "host.layer")),
        };
        host.AddObject(drawable);
        var outer = new Scene(64, 48, "outer") { Uri = new Uri(Path.Combine(_directory, "outer.scene")) };
        outer.Children.Add(host);
        node.Object.Value.ReferencedScene.Expression = SceneReferenceTestHelper.Create(outer, referenced);
        Assert.That(model.FindHierarchicalParent<Element>(), Is.SameAs(host), "The graph must belong to the intended host.");
        return outer;
    }

    private static (GraphModel Model, SceneNode Node) CreateGraph()
    {
        var model = new GraphModel();
        var node = new SceneNode();
        model.Nodes.Add(node);
        return (model, node);
    }

    // How many objects of the referenced scene were drawn.
    private static int Evaluate(GraphModel model, SceneNode node, MediaTime time)
    {
        using var snapshot = new GraphSnapshot();
        var context = new CompositionContext(time);
        snapshot.Build(model, context);
        snapshot.Evaluate(context);
        var resource = (SceneNode.Resource)snapshot.GetResource(snapshot.FindSlotIndex(node))!;
        return resource.SceneResource?.Frame?.Objects.Length ?? -1;
    }
}
