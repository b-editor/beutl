using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// The editor stores a scene reference as an expression and leaves the property's value null (#3374), so these clips
// are built the way it builds them. The referenced scene's length bounds edits as a media file's length does (#3844).
[TestFixture]
public sealed class SceneReferenceEditTests
{
    private static readonly string[] s_sourceTypes = [nameof(SceneDrawable), nameof(SceneSound)];

    private SceneHistoryHarness _harness = null!;
    private Scene _referenced = null!;
    private ElementSlipService _slip = null!;
    private ElementResizeService _resize = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_scene_reference", start: MediaTime.Zero, duration: Seconds(60));
        _referenced = new Scene { Duration = { Value = Seconds(5) } };
        _slip = new ElementSlipService(_harness.History);
        _resize = new ElementResizeService(_harness.History);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);

    private static IExpressionProperty<Scene?> ReferencedScene(EngineObject source)
        => source is SceneDrawable drawable ? drawable.ReferencedScene : ((SceneSound)source).ReferencedScene;

    // A clip whose source names no scene yet, ready for whichever form of reference a test gives it.
    private (Element Element, EngineObject Source) AddUnreferencedClip(string sourceType, double start, double length, double offset)
    {
        EngineObject source = sourceType == nameof(SceneDrawable) ? new SceneDrawable() : new SceneSound();
        ((ITimeMappedSource)source).OffsetPosition.Value = Seconds(offset);
        Element element = _harness.AddElement(Seconds(start), Seconds(length));
        element.AddObject(source);
        return (element, source);
    }

    private (Element Element, ITimeMappedSource Source) AddClip(string sourceType, double start, double length, double offset = 0)
    {
        (Element element, EngineObject source) = AddUnreferencedClip(sourceType, start, length, offset);
        ReferencedScene(source).Expression = SceneReferenceTestHelper.Create(_harness.Scene, _referenced);
        return (element, (ITimeMappedSource)source);
    }

    [TestCaseSource(nameof(s_sourceTypes))]
    public void Slip_StopsAtTheEndOfTheReferencedScene(string sourceType)
    {
        (Element element, ITimeMappedSource source) = AddClip(sourceType, 0, 3);

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(source.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
        });
    }

    [Test]
    public void Slip_SceneSoundAtHalfSpeed_ReadsLessOfTheScene()
    {
        (Element element, ITimeMappedSource source) = AddClip(nameof(SceneSound), 0, 3);
        source.Speed!.Value = 50;

        _slip.Slip(_harness.Scene, [element], Seconds(20));

        Assert.That(source.OffsetPosition.Value, Is.EqualTo(Seconds(3.5)));
    }

    // A scene's picture and sound usually sit on two linked clips, and one that refused would hold back the other.
    [Test]
    public void Slip_ThePictureAndSoundOfAScene_MoveTogether()
    {
        (Element picture, ITimeMappedSource pictureSource) = AddClip(nameof(SceneDrawable), 0, 3);
        (Element sound, ITimeMappedSource soundSource) = AddClip(nameof(SceneSound), 4, 3);

        bool applied = _slip.Slip(_harness.Scene, [picture, sound], Seconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(pictureSource.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
            Assert.That(soundSource.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
        });
    }

    [TestCaseSource(nameof(s_sourceTypes))]
    public void Resize_StopsAtTheEndOfTheReferencedScene(string sourceType)
    {
        (Element element, _) = AddClip(sourceType, 0, 2, offset: 1);

        _resize.Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(8), 0) { ClampToSource = true }]);

        Assert.That(element.Length.Value, Is.EqualTo(Seconds(4)));
    }

    // Projects saved while the length was unknown can hold clips that run past the scene's end.
    [TestCaseSource(nameof(s_sourceTypes))]
    public void Resize_AClipPastTheEndOfTheReferencedScene_CanShrinkButNotGrow(string sourceType)
    {
        (Element element, _) = AddClip(sourceType, 0, 8);

        _resize.Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(9), 0) { ClampToSource = true }]);
        MediaTime afterGrowing = element.Length.Value;
        _resize.Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(6), 0) { ClampToSource = true }]);

        Assert.Multiple(() =>
        {
            Assert.That(afterGrowing, Is.EqualTo(Seconds(8)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(6)));
        });
    }

    [TestCaseSource(nameof(s_sourceTypes))]
    public void OriginalDuration_IsWhatRemainsOfTheReferencedScene(string sourceType)
    {
        (Element element, _) = AddClip(sourceType, 0, 2, offset: 1);

        Assert.Multiple(() =>
        {
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.True);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(4)));
        });
    }

    [TestCaseSource(nameof(s_sourceTypes))]
    public void Roll_KeepsTheBackPieceContinuousWithinTheReferencedScene(string sourceType)
    {
        (Element front, _) = AddClip(sourceType, 0, 2);
        (Element back, ITimeMappedSource backSource) = AddClip(sourceType, 2, 2, offset: 2);

        bool applied = _resize.Roll(_harness.Scene, [new ElementTrimPair(front, back)], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(front.Length.Value, Is.EqualTo(Seconds(3)));
            Assert.That(back.Start.Value, Is.EqualTo(Seconds(3)));
            Assert.That(backSource.OffsetPosition.Value, Is.EqualTo(Seconds(3)));
        });
    }

    // The reference plays, so a value stored beside it says nothing about the length.
    [TestCaseSource(nameof(s_sourceTypes))]
    public void Slip_ReadsTheReferencedSceneRatherThanAStoredValue(string sourceType)
    {
        (Element element, ITimeMappedSource source) = AddClip(sourceType, 0, 3);
        ReferencedScene((EngineObject)source).Value = new Scene { Duration = { Value = Seconds(10) } };

        _slip.Slip(_harness.Scene, [element], Seconds(20));

        Assert.That(source.OffsetPosition.Value, Is.EqualTo(Seconds(2)));
    }

    // A reference that finds nothing plays nothing, whatever value is stored, so its length is unknown as an offline
    // file's is.
    [TestCaseSource(nameof(s_sourceTypes))]
    public void Slip_AReferenceThatFindsNothing_HasNoBound(string sourceType)
    {
        (Element element, EngineObject source) = AddUnreferencedClip(sourceType, 0, 3, 0);
        // The project exists, but holds no scene with the id below.
        _ = SceneReferenceTestHelper.Create(_harness.Scene, _referenced);
        IExpressionProperty<Scene?> reference = ReferencedScene(source);
        reference.Value = new Scene { Duration = { Value = Seconds(10) } };
        reference.Expression = new ReferenceExpression<Scene?>(Guid.NewGuid());

        bool applied = _slip.Slip(_harness.Scene, [element], Seconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(((ITimeMappedSource)source).OffsetPosition.Value, Is.EqualTo(Seconds(20)));
            Assert.That(SlippableMedia.Collect(element).Single().Total, Is.Null);
        });
    }

    // Reading another object's property can pick another scene as the clip plays, so its length cannot bound an edit.
    [TestCaseSource(nameof(s_sourceTypes))]
    public void AnExpressionThatPicksTheScene_RejectsOffsetWritesButAllowsResize(string sourceType)
    {
        (Element element, EngineObject source) = AddUnreferencedClip(sourceType, 0, 2, 0);
        ReferencedScene(source).Expression = new ReferenceExpression<Scene?>(Guid.NewGuid(), nameof(SceneSound.ReferencedScene));

        bool slipped = _slip.Slip(_harness.Scene, [element], Seconds(1));
        _resize.Resize(_harness.Scene, [new(element, element.Start.Value, Seconds(8), 0) { ClampToSource = true }]);

        Assert.Multiple(() =>
        {
            Assert.That(slipped, Is.False);
            Assert.That(((ITimeMappedSource)source).OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(8)));
        });
    }
}
