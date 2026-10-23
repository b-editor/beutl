using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.Core;

// Searches, Id lookups and duplication walk below an object the same way, and never up to its parent.
[TestFixture]
public class ChildObjectsTests
{
    [Test]
    public void FindById_FromAScene_DoesNotSearchTheRestOfTheProject()
    {
        var project = new Project();
        var first = new Scene();
        var second = new Scene();
        project.Items.Add(first);
        project.Items.Add(second);
        var element = new Element();
        var shape = new RectShape();
        element.AddObject(shape);
        second.Children.Add(element);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.FindById(shape.Id.Value), Is.Null);
            Assert.That(second.FindById(shape.Id.Value), Is.SameAs(shape));
            Assert.That(project.FindById(shape.Id.Value), Is.SameAs(shape));
        }
    }

    [Test]
    public void SearchAndFind_ReachKeyFramesAndNodePortValues()
    {
        var graph = new GraphModel();
        var pen = new Pen();
        Source(graph, pen);
        var portKeyFrames = TestAnimations.Linear(0f, 1f, MediaTime.FromSeconds(1));
        ((IAnimatablePropertyAdapter)Source(graph, 0f, "Amount").Property!).Animation = portKeyFrames;
        var shape = new RectShape();
        var keyFrames = TestAnimations.Linear(0f, 1f, MediaTime.FromSeconds(1));
        shape.Width.SetKeyFrameAnimation(keyFrames);
        var element = new Element();
        element.AddObject(shape);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new ObjectSearcher(graph, v => v is Pen).Search(), Is.SameAs(pen));
            Assert.That(graph.FindById(pen.Id.Value), Is.SameAs(pen));
            Assert.That(new ObjectSearcher(graph, v => v is IKeyFrameAnimation).Search(), Is.SameAs(portKeyFrames));
            Assert.That(new ObjectSearcher(element, v => v is IKeyFrameAnimation).Search(), Is.SameAs(keyFrames));
            Assert.That(new ObjectSearcher(element, v => v is Beutl.IProperty { Name: "Width" }).Search(),
                Is.SameAs(shape.Width));
        }
    }

    [Test]
    public void Regenerate_GivesNodePortValuesNewIds()
    {
        var graph = new GraphModel();
        var pen = new Pen();
        Source(graph, pen);

        ObjectRegenerator.Regenerate(graph, out GraphModel copy);
        var copied = (Pen?)new ObjectSearcher(copy, v => v is Pen).Search();

        Assert.That(copied, Is.Not.Null);
        Assert.That(copied!.Id.Value, Is.Not.EqualTo(pen.Id.Value));
    }
}
