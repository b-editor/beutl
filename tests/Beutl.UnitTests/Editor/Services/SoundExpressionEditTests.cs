using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

// Playback evaluates an expression on a sound's offset or speed (#3833). Slip and Trim read the stored values, which
// then do not say what plays, so moving the offset by them would not change what is heard. A split needs only where
// the front piece stops, so it reads a speed expression at the cut (#3851).
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

    private (Element Element, Sound Sound) AddClip(double start = 0)
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

        Element element = _harness.AddElement(MediaTime.FromSeconds(start), MediaTime.FromSeconds(2));
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

    private MediaTime BackOffset(Element front)
    {
        Element back = _harness.Scene.Children.Single(child => child != front);
        return back.Objects.OfType<Sound>().Single().OffsetPosition.Value;
    }

    // One second into the clip.
    private MediaTime SplitAndReadBackOffset(Element element)
    {
        _structure.Split(_harness.Scene, [element], element.Start.Value + MediaTime.FromSeconds(1));
        return BackOffset(element);
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

        // Without a mapping to follow, the back piece moves by the local time alone.
        Assert.That(SplitAndReadBackOffset(element), Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Split_SpeedExpression_ContinuesAtItsValue()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Value = 300;
        sound.Speed.Expression = Expression.Create<float>("200");

        Assert.That(SplitAndReadBackOffset(element), Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Split_SpeedExpressionOverKeyFrames_ContinuesAtItsValue()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.SetKeyFrameAnimation(TrimTestMedia.CreateSpeedRamp(useGlobalClock: false));
        sound.Speed.Expression = Expression.Create<float>("200");

        Assert.That(SplitAndReadBackOffset(element), Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    // Time is the clip's local time, one second at the cut here, however late the clip starts.
    [Test]
    public void Split_SpeedExpressionThatChanges_ReadsItsValueAtTheCut()
    {
        (Element element, Sound sound) = AddClip(start: 3);
        sound.Speed.Expression = Expression.Create<float>("100 + 100 * Time");

        Assert.That(SplitAndReadBackOffset(element), Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    // Playback falls back to the stored speed when the script fails, so the split must too.
    [Test]
    public void Split_SpeedExpressionThatFails_ContinuesAtTheStoredSpeed()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Value = 300;
        sound.Speed.Expression = Expression.Create<float>("this does not compile");

        Assert.That(SplitAndReadBackOffset(element), Is.EqualTo(MediaTime.FromSeconds(3)));
    }

    [Test]
    public void Split_SpeedExpressionWithoutAFiniteValue_ShiftsByLocalTime()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Expression = Expression.Create<float>("float.NaN");

        Assert.That(SplitAndReadBackOffset(element), Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void SilenceSplit_SpeedExpression_ContinuesAtItsValue()
    {
        (Element element, Sound sound) = AddClip();
        sound.Speed.Expression = Expression.Create<float>("200");

        new SilenceSplitService(_harness.History).Apply(_harness.Scene, element,
            [new TimeRange(MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(0.5))], SilenceSplitMode.DeleteSilence, false);

        Assert.That(BackOffset(element), Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    // Background preparation works on private copies of the clips, where a speed that reads another clip finds nothing.
    [Test]
    public async Task SilenceSplitInTheBackground_SpeedReadingAnotherClip_ContinuesAtItsValue()
    {
        (Element element, Sound sound) = AddClip();
        _ = SceneReferenceTestHelper.Create(_harness.Scene, new Scene());
        var provider = new SourceSound { Speed = { Value = 200 } };
        Element providerElement = _harness.AddElement(MediaTime.FromSeconds(5), MediaTime.FromSeconds(1), zIndex: 1);
        providerElement.AddObject(provider);
        sound.Speed.Expression = new ReferenceExpression<float>(provider.Id.Value, nameof(Sound.Speed));

        await new SilenceSplitService(_harness.History).ApplyAsync(_harness.Scene, element,
            [new TimeRange(MediaTime.FromSeconds(0.5), MediaTime.FromSeconds(0.5))], SilenceSplitMode.DeleteSilence, false);

        Element back = _harness.Scene.Children
            .Where(child => child != providerElement)
            .OrderBy(child => child.Start.Value)
            .Last();
        Assert.That(back.Objects.OfType<Sound>().Single().OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }
}
