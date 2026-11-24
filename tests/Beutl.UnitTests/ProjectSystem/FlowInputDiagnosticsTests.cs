using Beutl.Audio;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Primitives;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class FlowInputDiagnosticsTests
{
    [TestCase(typeof(Scene3D))]
    [TestCase(typeof(DrawableGroup))]
    [TestCase(typeof(DrawableDecorator))]
    [TestCase(typeof(DrawableTimeController))]
    [TestCase(typeof(SoundGroup))]
    [TestCase(typeof(DrawableObject3D))]
    public void CompatiblePortalInputOnlyNeedsToReachOnePartOfTheClip(Type operatorType)
    {
        var scene = new Scene();
        var op = (EngineObject)Activator.CreateInstance(operatorType)!;
        Element receiver = AddClip(scene, op, layer: 0, start: 0, duration: 4);
        var portal = (PortalObject)receiver.Objects[0];
        portal.Count.Value = 1;

        EngineObject source = op switch
        {
            Scene3D => new Cube3D(),
            SoundGroup => new SourceSound(),
            _ => new RectShape()
        };
        Element input = AddClip(scene, source, layer: 2, start: 1, duration: 1);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { op }));

        input.ZIndex.Value = 1;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);

        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsSolo = { Value = true } });
        scene.Layers.Add(new TimelineLayer
        {
            ZIndex = { Value = 1 },
            IsVideoMuted = { Value = true },
            IsAudioMuted = { Value = true }
        });
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);

        input.Start.Value = MediaTime.FromSeconds(4);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { op }));
    }

    [Test]
    public void Scene3DNeedsAnObjectBeyondItsCameraAndLights()
    {
        var scene = new Scene();
        var scene3D = new Scene3D();
        scene3D.Lights.Add(new DirectionalLight3D());
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 0, duration: 2);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        AddClip(scene, new RectShape(), layer: 1, start: 0, duration: 2);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));

        scene3D.Objects.Add(new Cube3D());
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void SameElementInputsFollowClearAndEarlierOperators()
    {
        var scene = new Scene();
        Element receiver = AddClip(scene, new Cube3D(), layer: 0, start: 0, duration: 2);
        var first = new Scene3D();
        var second = new Scene3D();
        receiver.AddObject(first);
        receiver.AddObject(second);
        var firstPortal = (PortalObject)receiver.Objects[1];

        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { second }));
        firstPortal.Clear.Value = true;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void ASecondPortalCannotRetakeAClipThatTheFirstAlreadyTook()
    {
        var scene = new Scene();
        var group = new DrawableGroup();
        Element receiver = AddClip(scene, group, layer: 0, start: 0, duration: 2);
        var firstPortal = new PortalObject { Count = { Value = 1 } };
        var secondPortal = (PortalObject)receiver.Objects[0];
        secondPortal.Count.Value = 1;
        secondPortal.Clear.Value = true;
        receiver.Objects.Insert(0, firstPortal);
        AddClip(scene, new RectShape(), layer: 1, start: 0, duration: 2);
        AddClip(scene, new PortalObject { Count = { Expression = new StringExpression<int>("1") } },
            layer: 2, start: 0, duration: 2);

        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));
        firstPortal.IsEnabled.Value = false;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void AClipInThePortalSpanCanPassThroughInputFromFartherBelow()
    {
        var scene = new Scene();
        var scene3D = new Scene3D();
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 0, duration: 3);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        AddClip(scene, new PortalObject { Count = { Value = 1 } }, layer: 1, start: 1, duration: 2);
        Element cube = AddClip(scene, new Cube3D(), layer: 2, start: 0, duration: 1);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));

        cube.Start.Value = MediaTime.FromSeconds(1);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void APortalWithAnExpressionPreservesPossibleInputs()
    {
        var scene = new Scene();
        var group = new DrawableGroup();
        Element receiver = AddClip(scene, group, layer: 0, start: 0, duration: 2);
        var portal = (PortalObject)receiver.Objects[0];
        portal.Count.Expression = new StringExpression<int>("1");
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        Element source = AddClip(scene, new RectShape(), layer: 1, start: 0, duration: 2);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);

        source.Objects[0].IsEnabled.Value = false;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));
    }

    [Test]
    public void OverlapBetweenTwoVideoFramesDoesNotSupplyAVisualInput()
    {
        var scene = new Scene();
        var group = new DrawableGroup();
        Element receiver = AddClip(scene, group, layer: 0, start: 0, duration: 1);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        Element input = AddClip(scene, new RectShape(), layer: 1, start: 0.005, duration: 0.005);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        input.Length.Value = MediaTime.FromSeconds(0.05);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void TransitionOutputCanSupplyAVisualInput()
    {
        var scene = new Scene();
        var group = new DrawableGroup();
        Element receiver = AddClip(scene, group, layer: 0, start: 0, duration: 2);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        Element input = AddClip(scene, new Cube3D(), layer: 1, start: 0, duration: 2);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        input.EnterTransition.Value = new FadeTransition();
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    private static Element AddClip(Scene scene, EngineObject obj, int layer, double start, double duration)
    {
        var element = new Element
        {
            ZIndex = { Value = layer },
            Start = { Value = MediaTime.FromSeconds(start) },
            Length = { Value = MediaTime.FromSeconds(duration) }
        };
        element.AddObject(obj);
        scene.Children.Add(element);
        return element;
    }
}
