using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Skyboxes;
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
    public void Scene3DCanUseAnEnabledSkyboxWithoutAnObject()
    {
        var scene = new Scene();
        var skybox = new ProceduralSkybox();
        var scene3D = new Scene3D { Skybox = { Value = skybox } };
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 0, duration: 2);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);

        skybox.IsEnabled.Value = false;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));
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

        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        secondPortal.Count.Value = 2;
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<DrawableGroup.Resource>().Single().Children, Is.Empty);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        AddClip(scene, new Cube3D(), layer: 3, start: 0, duration: 2);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));
        secondPortal.Count.Value = 1;

        Element matte = AddClip(scene, new RectShape(), layer: 3, start: 0, duration: 2);
        matte.MatteMode.Value = MatteMode.Alpha;
        matte.MatteSourceKind.Value = MatteSourceKind.Layer;
        matte.MatteSourceZIndex.Value = 4;
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<DrawableGroup.Resource>().Single().Children, Is.Empty);
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

        source.IsVisibleInOutput.Value = false;
        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<DrawableGroup.Resource>().Single().Children, Is.Empty);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        source.IsVisibleInOutput.Value = true;
        source.Objects[0].IsEnabled.Value = false;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        source.Objects[0].IsEnabled.Value = true;
        portal.Count.Expression = new StringExpression<int>("0");
        receiver.Objects.Insert(1, new PortalObject { Count = { Value = 1 }, Clear = { Value = true } });
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<DrawableGroup.Resource>().Single().Children, Has.Count.EqualTo(1));
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void FixedPortalsConsumeInputsInsideAnExpressionPortal()
    {
        var scene = new Scene();
        var scene3D = new Scene3D();
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 0, duration: 2);
        ((PortalObject)receiver.Objects[0]).Count.Expression = new StringExpression<int>("1");
        Element nested = AddClip(scene, new PortalObject { Count = { Value = 1 } }, layer: 1, start: 0, duration: 2);
        var clear = new PortalObject { Count = { Value = 1 }, Clear = { Value = true } };
        nested.Objects.Add(clear);
        AddClip(scene, new Cube3D(), layer: 2, start: 0, duration: 2);

        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<Scene3D.Resource>().Single().Objects, Is.Empty);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));

        clear.Clear.Value = false;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
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

    [Test]
    public void AnEmptyMatteClipStillSuppliesItsDrawablePresenter()
    {
        var scene = new Scene();
        var group = new DrawableGroup();
        Element receiver = AddClip(scene, group, layer: 0, start: 0, duration: 2);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        Element matte = new() { Length = { Value = MediaTime.FromSeconds(2) }, ZIndex = { Value = 1 } };
        scene.Children.Add(matte);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        matte.MatteMode.Value = MatteMode.Alpha;
        matte.MatteSourceKind.Value = MatteSourceKind.Layer;
        matte.MatteSourceZIndex.Value = 2;
        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<DrawableGroup.Resource>().Single().Children, Has.Count.EqualTo(1));
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void ASeparateMatteReadCanUseHiddenPortalInputs()
    {
        var scene = new Scene();
        var group = new DrawableGroup();
        Element receiver = AddClip(scene, group, layer: 1, start: 0, duration: 2);
        receiver.IsVisibleInOutput.Value = false;
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        Element input = AddClip(scene, new RectShape(), layer: 2, start: 0, duration: 2);
        input.IsVisibleInOutput.Value = false;

        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects, Is.Empty);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));

        Element target = AddClip(scene, new RectShape(), layer: 0, start: 0, duration: 2);
        target.MatteMode.Value = MatteMode.Alpha;
        target.MatteSourceKind.Value = MatteSourceKind.Layer;
        target.MatteSourceZIndex.Value = 1;

        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
        receiver.IsVisibleInOutput.Value = true;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
        target.MatteMode.Value = MatteMode.None;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { group }));
    }

    [Test]
    public void AnOutgoingClipSuppliesInputOnlyUntilItsTransitionRetiresIt()
    {
        var scene = new Scene();
        var scene3D = new Scene3D();
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 7, duration: 1);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        AddClip(scene, new Cube3D(), layer: 1, start: 0, duration: 9);
        Element incoming = AddClip(scene, new RectShape(), layer: 1, start: 2, duration: 8);
        incoming.EnterTransition.Value = new FadeTransition();
        incoming.ExitTransition.Value = new FadeTransition();
        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(7)).Objects.OfType<Scene3D.Resource>().Single().Objects, Is.Empty);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));

        receiver.Start.Value = MediaTime.Zero;
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<Scene3D.Resource>().Single().Objects, Has.Count.EqualTo(1));
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void ATransitionPresenterDoesNotExposeItsSideAsA3DInput()
    {
        var scene = new Scene();
        var scene3D = new Scene3D();
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 0, duration: 0.25);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        Element input = AddClip(scene, new Cube3D(), layer: 1, start: 0, duration: 2);
        input.EnterTransition.Value = new FadeTransition();
        using var compositor = new SceneCompositor(scene, SceneRenderServices.Empty);
        Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).Objects.OfType<Scene3D.Resource>().Single().Objects, Is.Empty);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));

        receiver.Start.Value = MediaTime.FromSeconds(0.5);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASideOfATransitionUsesTheOutputOfTheClipItsPortalReaches(bool held)
    {
        var scene = new Scene();
        var scene3D = new Scene3D();
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 0, duration: held ? 1.02 : 0.25);
        ((PortalObject)receiver.Objects[0]).Count.Value = 1;
        if (held) receiver.ExitTransition.Value = new FadeTransition();
        else receiver.EnterTransition.Value = new FadeTransition();

        // Only the held edge of the exit side reaches this source, between two rendered frames.
        Element input = AddClip(scene, new Cube3D(), layer: 1, start: held ? 0.98 : 0, duration: held ? 0.01 : 2);
        var clear = new PortalObject { Clear = { Value = true } };
        input.Objects.Add(clear);
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));

        clear.Clear.Value = false;
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.Empty);
    }

    [Test]
    public void IdenticalTransitionReadsReuseTheirConsumptionAcrossPortalSides()
    {
        const int stackedLayers = 12;
        var scene = new Scene();
        var scene3D = new Scene3D();
        AddClip(scene, new PortalObject { Count = { Value = 1 } }, layer: 0, start: 0, duration: 4);
        Element receiver = AddClip(scene, scene3D, layer: 0, start: 1, duration: 4);
        receiver.EnterTransition.Value = new FadeTransition();
        var clearingPortal = (PortalObject)receiver.Objects[0];
        clearingPortal.Count.Value = stackedLayers + 1;
        clearingPortal.Clear.Value = true;
        receiver.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });

        for (int layer = 1; layer <= stackedLayers; layer++)
        {
            AddClip(scene, new PortalObject { Count = { Value = 1 } }, layer, start: 0, duration: 4);
            Element incoming = AddClip(scene, new PortalObject { Count = { Value = 1 } }, layer, start: 1, duration: 4);
            incoming.EnterTransition.Value = new FadeTransition();
        }

        Element source = AddClip(scene, new Cube3D(), layer: stackedLayers + 1, start: 0, duration: 5);
        var probe = new CountingGraphicsObject();
        source.Objects.Add(probe);

        // The incoming side's second Portal cannot retake the cube consumed by its first Portal.
        Assert.That(FlowInputDiagnostics.FindMissingInputs(scene, receiver), Is.EqualTo(new[] { scene3D }));
        Assert.That(probe.TargetQueries, Is.InRange(1, 128), "Equivalent reads of stacked transitions should reuse the deepest input evaluation.");
    }

    [SuppressResourceClassGeneration]
    private sealed class CountingGraphicsObject : EngineObject
    {
        public int TargetQueries { get; private set; }

        public override CompositionTarget GetCompositionTarget()
        {
            TargetQueries++;
            return CompositionTarget.Graphics;
        }
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
