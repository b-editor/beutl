using Beutl.Composition;
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
