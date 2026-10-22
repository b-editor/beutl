using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// A copy of a clip holds its file-backed objects itself, and keeps referencing a scene it draws (#3288).
[TestFixture]
public class CopiedFileBackedObjectsTests
{
    private SceneHistoryHarness _harness = null!;

    [SetUp]
    public void Setup()
        => _harness = new SceneHistoryHarness("beutl_copied_files", duration: TimeSpan.FromSeconds(60));

    [TearDown]
    public void TearDown() => _harness.Dispose();

    [Test]
    public void Regenerate_GivesTheCopyItsOwnFileBackedObject()
    {
        string file = Path.Combine(_harness.BasePath, "anim #1 ア.json");
        Element source = _harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        source.AddObject(ShapeWithKeyFrameAt(6, file));

        ObjectRegenerator.Regenerate(source, out Element copy);

        Assert.Multiple(() =>
        {
            Assert.That(Animation(copy).Uri, Is.Null);
            Assert.That(Animation(copy).Id, Is.Not.EqualTo(Animation(source).Id));
            Assert.That(KeyTimes(copy), Is.EqualTo(new[] { TimeSpan.FromSeconds(6) }));
            Assert.That(Animation(source).Uri, Is.EqualTo(new Uri(file)));
        });
    }

    // Both halves of a split used to write the animation file, so the front half reloaded with the back half's times.
    [Test]
    public void Split_GivesTheBackHalfItsOwnCopyOfAnAnimationFile()
    {
        string file = Path.Combine(_harness.BasePath, "anim.json");
        Element front = _harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        front.AddObject(ShapeWithKeyFrameAt(6, file));
        _harness.History.Commit("Setup");

        Element back = Split(front, 4);
        SaveEveryElement();

        Assert.Multiple(() =>
        {
            Assert.That(KeyTimes(Reload(front)), Is.EqualTo(new[] { TimeSpan.FromSeconds(6) }));
            Assert.That(KeyTimes(Reload(back)), Is.EqualTo(new[] { TimeSpan.FromSeconds(2) }));
            Assert.That(Animation(Reload(back)).Uri, Is.Null);
            Assert.That(CoreSerializer.RestoreFromUri<KeyFrameAnimation<float>>(new Uri(file)).KeyFrames.Single().KeyTime,
                Is.EqualTo(TimeSpan.FromSeconds(6)));
        });
    }

    // The back half used to draw a copy of the scene with new Ids and shifted keyframes, and to save it over the scene.
    [Test]
    public void Split_KeepsReferencingTheSceneTheClipDraws()
    {
        string folder = Path.Combine(_harness.BasePath, "scene #1 ア");
        var scene = new Scene(100, 100, "B") { Uri = new Uri(Path.Combine(folder, "B.scene")) };
        var inner = new Element { Length = TimeSpan.FromSeconds(10), Uri = new Uri(Path.Combine(folder, "inner.belm")) };
        inner.AddObject(ShapeWithKeyFrameAt(6));
        inner.AddObject(new CopyRenewalProbe());
        scene.Children.Add(inner);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        Element front = _harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var drawable = new SceneDrawable();
        drawable.ReferencedScene.Value = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        front.AddObject(drawable);
        _harness.History.Commit("Setup");

        Element back = Split(front, 4);
        Scene drawn = back.Objects.OfType<SceneDrawable>().Single().ReferencedScene.Value!;
        SaveEveryElement();
        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Uri, Is.EqualTo(scene.Uri));
            Assert.That(drawn.Id, Is.EqualTo(scene.Id));
            Assert.That(drawn.Children.Single().Id, Is.EqualTo(inner.Id));
            Assert.That(KeyTimes(drawn.Children.Single()), Is.EqualTo(new[] { TimeSpan.FromSeconds(6) }));
            Assert.That(drawn.Children.Single().Objects.OfType<CopyRenewalProbe>().Single().Renewals, Is.Zero);
            Assert.That(reloaded.Id, Is.EqualTo(scene.Id));
            Assert.That(reloaded.Children.Single().Id, Is.EqualTo(inner.Id));
            Assert.That(KeyTimes(reloaded.Children.Single()), Is.EqualTo(new[] { TimeSpan.FromSeconds(6) }));
        });
    }

    private Element Split(Element target, double seconds)
    {
        Element[] before = [.. _harness.Scene.Children];
        new ElementStructureService(_harness.History).Split(_harness.Scene, [target], TimeSpan.FromSeconds(seconds));
        return _harness.Scene.Children.Except(before).Single();
    }

    // As an explicit save writes them, with the files their objects reference.
    private void SaveEveryElement()
    {
        foreach (Element element in _harness.Scene.Children)
            CoreSerializer.StoreToUri(element, element.Uri!);
    }

    private static RectShape ShapeWithKeyFrameAt(double seconds, string? file = null)
    {
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float> { Uri = file is null ? null : new Uri(file) };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(seconds), Value = 50 });
        shape.Width.SetKeyFrameAnimation(animation);
        return shape;
    }

    private static KeyFrameAnimation<float> Animation(Element element)
        => element.Objects.OfType<RectShape>().Single().Width.GetKeyFrameAnimation()!;

    private static TimeSpan[] KeyTimes(Element element) => [.. Animation(element).KeyFrames.Select(k => k.KeyTime)];

    private static Element Reload(Element element) => CoreSerializer.RestoreFromUri<Element>(element.Uri!);
}
