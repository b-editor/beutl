using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class SceneSnapshotTests
{
    [Test]
    public void Create_CopiesSceneAudioLayoutAndRates()
    {
        var scene = new Scene(320, 240, "Surround")
        {
            FrameRate = { Value = new Rational(60) },
            SampleRate = { Value = 48000 },
            ChannelLayout = { Value = SpeakerLayout.Surround51 },
        };
        Scene snapshot = SceneSnapshot.Create(scene);
        scene.FrameRate.Value = new Rational(30);
        scene.SampleRate.Value = 44100;
        scene.ChannelLayout.Value = SpeakerLayout.Stereo;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FrameRate.Value, Is.EqualTo(new Rational(60)));
            Assert.That(snapshot.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(snapshot.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
        });
    }

    [Test]
    public void Create_PreservesANumericLayerNameThatNowMatchesItsRow()
    {
        var layer = new TimelineLayer { ZIndex = { Value = 1 }, Name = { Value = "2" } };
        var scene = new Scene { Layers = { layer } };
        layer.ZIndex.Value = 2;

        Assert.That(SceneSnapshot.Create(scene).Layers.Single().Name.Value, Is.EqualTo("2"));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Create_PreservesProjectNameReferencesAndIsolatesLaterEdits(bool stringExpression, bool throughOtherScene)
    {
        var scene = new Scene(320, 240, "Export");
        var other = new Scene(320, 240, "Other");
        var project = new Project { Name = { Value = "Project at export" }, Items = { scene, other } };
        _ = new BeutlApplication { Projects = { project } };
        TextBlock source = AddText(throughOtherScene ? other : scene);
        source.Text.Expression = Reference(project, nameof(Project.Name), stringExpression);
        if (throughOtherScene)
        {
            AddText(scene).Text.Expression = Reference(source, nameof(TextBlock.Text), stringExpression);
        }

        Assert.That(EvaluateText(scene), Is.EqualTo("Project at export"));

        Scene snapshot = SceneSnapshot.Create(scene);
        project.Name.Value = "Project edited during export";

        Assert.Multiple(() =>
        {
            Assert.That(EvaluateText(scene), Is.EqualTo("Project edited during export"));
            Assert.That(EvaluateText(snapshot), Is.EqualTo("Project at export"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Create_PreservesReferencesToAnotherSceneAndItsContents(bool stringExpression)
    {
        var scene = new Scene(320, 240, "Export");
        var other = new Scene(320, 240, "Other at export");
        var project = new Project { Items = { scene, other } };
        _ = new BeutlApplication { Projects = { project } };
        TextBlock source = AddText(other);
        source.Text.Value = "Text at export";
        AddText(scene).Text.Expression = Reference(other, nameof(Scene.Name), stringExpression);
        AddText(scene).Text.Expression = Reference(source, nameof(TextBlock.Text), stringExpression);

        Assert.Multiple(() =>
        {
            Assert.That(EvaluateText(scene), Is.EqualTo("Other at export"));
            Assert.That(EvaluateText(scene, 1), Is.EqualTo("Text at export"));
        });

        Scene snapshot = SceneSnapshot.Create(scene);
        other.Name.Value = "Other edited during export";
        source.Text.Value = "Text edited during export";

        Assert.Multiple(() =>
        {
            Assert.That(EvaluateText(scene), Is.EqualTo("Other edited during export"));
            Assert.That(EvaluateText(scene, 1), Is.EqualTo("Text edited during export"));
            Assert.That(EvaluateText(snapshot), Is.EqualTo("Other at export"));
            Assert.That(EvaluateText(snapshot, 1), Is.EqualTo("Text at export"));
        });
    }

    // Silence analysis captures on the document thread and builds the copy on a worker after the document moved on.
    [Test]
    public async Task Materialize_OnAnotherThread_BuildsTheSceneAsCaptured()
    {
        var scene = new Scene(320, 240, "Export");
        var other = new Scene(320, 240, "Other at capture");
        var project = new Project { Name = { Value = "Project at capture" }, Items = { scene, other } };
        var application = new BeutlApplication { Projects = { project } };
        AddText(scene).Text.Expression = Reference(project, nameof(Project.Name), stringExpression: false);
        AddText(scene).Text.Expression = Reference(other, nameof(Scene.Name), stringExpression: false);

        SceneSnapshot.SceneCapture capture = SceneSnapshot.Capture(scene);
        project.Name.Value = "Project edited after capture";
        other.Name.Value = "Other edited after capture";
        int captureThread = Environment.CurrentManagedThreadId;
        (Scene snapshot, int materializeThread) = await Task.Run(
            () => (SceneSnapshot.Materialize(capture), Environment.CurrentManagedThreadId));

        Assert.Multiple(() =>
        {
            Assert.That(materializeThread, Is.Not.EqualTo(captureThread));
            Assert.That(snapshot.FindHierarchicalRoot(), Is.InstanceOf<BeutlApplication>().And.Not.SameAs(application));
            Assert.That(EvaluateText(snapshot), Is.EqualTo("Project at capture"));
            Assert.That(EvaluateText(snapshot, 1), Is.EqualTo("Other at capture"));
        });
    }

    private static IExpression<string?> Reference(CoreObject target, string propertyName, bool stringExpression)
        => stringExpression
            ? new StringExpression<string?>($"GetProperty<string>(\"{target.Id.Value}.{propertyName}\")")
            : new ReferenceExpression<string?>(target.Id.Value, propertyName);

    private static TextBlock AddText(Scene scene)
    {
        var text = new TextBlock();
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(text);
        scene.Children.Add(element);
        return text;
    }

    private static string? EvaluateText(Scene scene, int elementIndex = 0)
        => ((TextBlock)scene.Children[elementIndex].Objects.Single()).Text.Evaluate(new CompositionContext(MediaTime.Zero) { Fonts = TestFonts.Manager });
}
