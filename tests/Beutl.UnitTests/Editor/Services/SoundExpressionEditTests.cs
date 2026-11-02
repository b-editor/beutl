using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// Playback evaluates an expression on a sound's offset or speed (#3833). Edits read the stored values, which then do
// not say what plays, so moving the offset by them would not change what is heard.
[TestFixture(nameof(SourceSound))]
[TestFixture(nameof(SceneSound))]
public sealed class SoundExpressionEditTests(string soundType)
{
    private SceneHistoryHarness _harness = null!;
    private ElementSlipService _slip = null!;
    private ElementStructureService _structure = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_sound_expression", start: MediaTime.Zero, duration: MediaTime.FromSeconds(60));
        _slip = new ElementSlipService(_harness.History);
        _structure = new ElementStructureService(_harness.History);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    private (Element Element, Sound Sound) AddClip()
    {
        Sound sound;
        if (soundType == nameof(SourceSound))
        {
            sound = new SourceSound();
        }
        else
        {
            var sceneSound = new SceneSound();
            sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(
                _harness.Scene, new Scene { Duration = { Value = MediaTime.FromSeconds(30) } });
            sound = sceneSound;
        }

        Element element = _harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        element.AddObject(sound);
        return (element, sound);
    }

    private void AssertSlipIsRefused(Element element, Sound sound)
    {
        bool applied = _slip.Slip(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    // Without a mapping to follow, the back piece moves by the local time alone.
    private void AssertSplitShiftsByLocalTime(Element element)
    {
        _structure.Split(_harness.Scene, [element], MediaTime.FromSeconds(1));

        Element back = _harness.Scene.Children.Single(child => child != element);
        Assert.That(back.Objects.OfType<Sound>().Single().OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Slip_OffsetExpression_IsRefused()
    {
        (Element element, Sound sound) = AddClip();
        sound.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(1)");

        AssertSlipIsRefused(element, sound);
    }

    [Test]
    public void Slip_SpeedExpression_IsRefused()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Expression = Expression.Create<float>("200");

        AssertSlipIsRefused(element, sound);
    }

    // An expression on top of keyframes replaces them for audio, so their integral says nothing either.
    [Test]
    public void Slip_SpeedExpressionOverKeyFrames_IsRefused()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.SetKeyFrameAnimation(TrimTestMedia.CreateSpeedRamp(useGlobalClock: false));
        sound.Speed.Expression = Expression.Create<float>("200");

        AssertSlipIsRefused(element, sound);
    }

    [Test]
    public void Split_OffsetExpression_ShiftsByLocalTime()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Value = 200;
        sound.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(1)");

        AssertSplitShiftsByLocalTime(element);
    }

    [Test]
    public void Split_SpeedExpression_ShiftsByLocalTime()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Value = 300;
        sound.Speed.Expression = Expression.Create<float>("200");

        AssertSplitShiftsByLocalTime(element);
    }
}
