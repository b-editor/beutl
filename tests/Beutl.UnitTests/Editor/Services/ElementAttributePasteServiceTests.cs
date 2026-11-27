using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public sealed class ElementAttributePasteServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private ElementAttributePasteService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("paste_attributes", duration: MediaTime.FromSeconds(20));
        _service = new ElementAttributePasteService(_harness.History);
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    [Test]
    public void Audio_effects_only_are_copied_from_the_clipboard_snapshot()
    {
        var delay = new DelayEffect { DelayTime = { Value = 250 } };
        var group = new AudioEffectGroup { Children = { delay } };
        var sourceSound = new SourceSound { Effect = { Value = group }, Gain = { Value = 45 } };
        var source = AddClip(0, 0, sourceSound, new RectShape { FilterEffect = { Value = new Blur() } });
        var targetSound = new SourceSound { Gain = { Value = 70 }, OffsetPosition = { Value = MediaTime.FromSeconds(3) } };
        var targetShape = new RectShape();
        FilterEffect? targetVideoEffects = targetShape.FilterEffect.Value;
        var target = AddClip(3, 0, targetShape, targetSound);
        string json = CoreSerializer.SerializeToJsonString(source);
        delay.DelayTime.Value = 500;

        Paste(ElementAttributePasteService.ReadSource(json)!, ElementAttributeKinds.AudioEffects, target);

        var pasted = (DelayEffect)((AudioEffectGroup)targetSound.Effect.Value!).Children.Single();
        Assert.Multiple(() =>
        {
            Assert.That(pasted.DelayTime.Value, Is.EqualTo(250));
            Assert.That(pasted.Id.Value, Is.Not.EqualTo(delay.Id.Value));
            Assert.That(targetSound.Gain.Value, Is.EqualTo(70));
            Assert.That(targetSound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(targetShape.FilterEffect.Value, Is.SameAs(targetVideoEffects));
        });
    }

    [Test]
    public void Transform_only_keeps_target_animations_and_copies_base_values()
    {
        var sourceMove = new TranslateTransform(75, 20);
        Animate(sourceMove.X, false, (0, 5));
        Animate(sourceMove.Y, false, (0, 10));
        var sourceShape = new RectShape { Transform = { Value = sourceMove }, TransformOrigin = { Value = RelativePoint.TopLeft } };
        var targetMove = new TranslateTransform(10, 15);
        var targetShape = new EllipseShape { Transform = { Value = targetMove } };
        KeyFrameAnimation<float> movement = Animate(targetMove.X, false, (0.5, 12));
        KeyFrameAnimation<float> opacity = Animate(targetShape.Opacity, false, (1, 90));
        Element source = AddClip(0, 0, sourceShape);
        Element target = AddClip(3, 0, targetShape);

        Paste(source, ElementAttributeKinds.Transform, target);

        var pasted = (TranslateTransform)targetShape.Transform.Value!;
        var retained = pasted.X.GetKeyFrameAnimation()!;
        Assert.Multiple(() =>
        {
            Assert.That(pasted.X.Value, Is.EqualTo(75));
            Assert.That(pasted.Y.Value, Is.EqualTo(20));
            Assert.That(targetShape.TransformOrigin.Value, Is.EqualTo(RelativePoint.TopLeft));
            Assert.That(retained.Id.Value, Is.EqualTo(movement.Id.Value));
            Assert.That(retained.KeyFrames[0].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(retained.KeyFrames[0].Value, Is.EqualTo(12f));
            Assert.That(pasted.Y.GetKeyFrameAnimation(), Is.Null);
            Assert.That(targetShape.Opacity.GetKeyFrameAnimation(), Is.SameAs(opacity));
        });
    }

    [Test]
    public void Transform_only_keeps_a_target_transform_with_an_unmatched_animation()
    {
        var source = AddClip(0, 0, new RectShape { Transform = { Value = new ScaleTransform() } });
        var move = new TranslateTransform(10, 20);
        Animate(move.X, false, (0.5, 25));
        var targetShape = new RectShape { Transform = { Value = move } };
        var target = AddClip(3, 0, targetShape);

        Paste(source, ElementAttributeKinds.Transform, target);

        Assert.That(targetShape.Transform.Value, Is.SameAs(move));
    }

    [Test]
    public void Video_effects_only_skip_audio_targets_and_keep_the_target_transform()
    {
        var blur = new Blur { Sigma = { Value = new Size(6, 9) } };
        var sourceShape = new RectShape { FilterEffect = { Value = blur } };
        var source = AddClip(0, 0, sourceShape);
        var sound = new SourceSound();
        var audio = AddClip(3, 0, sound);
        var soundEffects = sound.Effect.Value;
        var targetShape = new EllipseShape();
        var video = AddClip(6, 0, targetShape);
        var transform = targetShape.Transform.Value;

        Paste(source, ElementAttributeKinds.VideoEffects, audio, video);

        Assert.Multiple(() =>
        {
            Assert.That(((Blur)targetShape.FilterEffect.Value!).Sigma.Value, Is.EqualTo(new Size(6, 9)));
            Assert.That(targetShape.Transform.Value, Is.SameAs(transform));
            Assert.That(sound.Effect.Value, Is.SameAs(soundEffects));
        });
    }

    [Test]
    public void Animations_only_keep_clip_relative_timing_on_both_clocks()
    {
        var sourceMove = new TranslateTransform(50, 10);
        var sourceShape = new RectShape { Transform = { Value = sourceMove }, Opacity = { Value = 80 } };
        var source = AddClip(4, 0, sourceShape);
        KeyFrameAnimation<float> sourceGlobal = Animate(sourceShape.Opacity, true, (4.5, 10), (5.5, 75));
        KeyFrameAnimation<float> sourceLocal = Animate(sourceMove.X, false, (0.5, 42), (1.5, 64));
        var targetMove = new TranslateTransform(12, 30);
        var targetShape = new EllipseShape { Transform = { Value = targetMove }, Opacity = { Value = 52 } };
        var target = AddClip(1, 0, targetShape);
        KeyFrameAnimation<float> untouched = Animate(targetMove.Y, false, (0.5, 18));

        Paste(ElementAttributePasteService.ReadSource(CoreSerializer.SerializeToJsonString(source))!, ElementAttributeKinds.Animations, target);

        var absolute = targetShape.Opacity.GetKeyFrameAnimation()!;
        var relative = targetMove.X.GetKeyFrameAnimation()!;
        Assert.Multiple(() =>
        {
            Assert.That(absolute.UseGlobalClock.Value, Is.True);
            Assert.That(absolute.KeyFrames.Select(key => key.KeyTime.Value), Is.EqualTo(new[] { MediaTime.FromSeconds(1.5), MediaTime.FromSeconds(2.5) }));
            Assert.That(absolute.KeyFrames.Select(key => key.Value), Is.EqualTo(new[] { 10f, 75f }));
            Assert.That(absolute.KeyFrames[0].Easing.Value, Is.TypeOf<SplineEasing>());
            Assert.That(absolute.KeyFrames[0].Id.Value, Is.Not.EqualTo(sourceGlobal.KeyFrames[0].Id.Value));
            Assert.That(relative.UseGlobalClock.Value, Is.False);
            Assert.That(relative.KeyFrames.Select(key => key.KeyTime.Value), Is.EqualTo(sourceLocal.KeyFrames.Select(key => key.KeyTime.Value)));
            Assert.That(targetShape.Opacity.Value, Is.EqualTo(52));
            Assert.That(targetShape.Transform.Value, Is.SameAs(targetMove));
            Assert.That(targetMove.X.Value, Is.EqualTo(12));
            Assert.That(targetMove.Y.GetKeyFrameAnimation(), Is.SameAs(untouched));
        });
    }

    [Test]
    public void Animations_paste_separated_keyframes_and_their_unit_changes_together()
    {
        var shape = new RectShape();
        var source = AddClip(0, 0, shape);
        var targetShape = new RectShape();
        var target = AddClip(3, 0, targetShape);
        var keys = new KeyFrameAnimation<RelativePoint> { UseGlobalClock = { Value = true } };
        keys.KeyFrames.Add(new KeyFrame<RelativePoint> { Value = { Value = RelativePoint.Center } });
        keys.KeyFrames.Add(new KeyFrame<RelativePoint>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = new RelativePoint(70, 20, RelativeUnit.Absolute) }
        });
        shape.TransformOrigin.SetKeyFrameAnimation(keys);
        ComponentAnimations.Separate(shape.TransformOrigin);

        Paste(ElementAttributePasteService.ReadSource(CoreSerializer.SerializeToJsonString(source))!, ElementAttributeKinds.Animations, target);

        var pasted = (ComponentAnimation<RelativePoint, float>)targetShape.TransformOrigin.GetPropertyAnimation()!;
        MediaTime[] expected = [MediaTime.FromSeconds(3), MediaTime.FromSeconds(4)];
        Assert.That(pasted.UnitSource.Value!.KeyFrames.Select(key => key.KeyTime.Value), Is.EqualTo(expected));
        Assert.That(pasted.Components[0].GetKeyFrameAnimation()!.KeyFrames.Select(key => key.KeyTime.Value), Is.EqualTo(expected));
    }

    [Test]
    public void All_changes_editable_targets_in_one_undo_step_and_skips_both_locks()
    {
        var sourceMove = new TranslateTransform(90, 15);
        Animate(sourceMove.X, true, (1, 60));
        var source = AddClip(0, 0, new RectShape { Transform = { Value = sourceMove }, FilterEffect = { Value = new Blur() } });
        var firstShape = new RectShape();
        var first = AddClip(3, 0, firstShape);
        var secondShape = new EllipseShape();
        var second = AddClip(6, 0, secondShape);
        var lockedShape = new RectShape();
        var locked = AddClip(9, 0, lockedShape);
        locked.IsLocked.Value = true;
        var lockedLayerShape = new RectShape();
        var lockedLayer = AddClip(3, 1, lockedLayerShape);
        _harness.AddLayer(1).IsLocked.Value = true;
        var originals = new[] { firstShape.Transform.Value, secondShape.Transform.Value, lockedShape.Transform.Value, lockedLayerShape.Transform.Value };
        _harness.History.Commit();
        int before = _harness.History.UndoCount;

        _service.Paste(_harness.Scene, source, [first, second, locked, lockedLayer], ElementAttributeKinds.All);

        var firstMove = (TranslateTransform)firstShape.Transform.Value!;
        var secondMove = (TranslateTransform)secondShape.Transform.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(firstMove.X.GetKeyFrameAnimation()!.KeyFrames[0].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
            Assert.That(secondMove.X.GetKeyFrameAnimation()!.KeyFrames[0].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(sourceMove.X.GetKeyFrameAnimation()!.KeyFrames[0].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(firstMove.Id.Value, Is.Not.EqualTo(secondMove.Id.Value));
            Assert.That(lockedShape.Transform.Value, Is.SameAs(originals[2]));
            Assert.That(lockedLayerShape.Transform.Value, Is.SameAs(originals[3]));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(before + 1));
        });

        _harness.History.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(firstShape.Transform.Value, Is.SameAs(originals[0]));
            Assert.That(secondShape.Transform.Value, Is.SameAs(originals[1]));
            Assert.That(firstShape.FilterEffect.Value, Is.TypeOf<FilterEffectGroup>());
            Assert.That(secondShape.FilterEffect.Value, Is.TypeOf<FilterEffectGroup>());
        });
        _harness.History.Redo();
        Assert.That(firstMove.X.GetKeyFrameAnimation()!.KeyFrames[0].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
    }

    private void Paste(Element source, ElementAttributeKinds kind, params Element[] targets)
    {
        _harness.History.Commit();
        _service.Paste(_harness.Scene, source, targets, kind);
    }

    private Element AddClip(double start, int zIndex, params EngineObject[] objects)
    {
        var clip = _harness.AddElement(MediaTime.FromSeconds(start), MediaTime.FromSeconds(2), zIndex);
        foreach (EngineObject obj in objects) clip.AddObject(obj);
        return clip;
    }

    private static KeyFrameAnimation<float> Animate(IProperty<float> property, bool global, params (double Time, float Value)[] keys)
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = global } };
        foreach (var (time, value) in keys)
        {
            animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.FromSeconds(time) },
                Value = { Value = value },
                Easing = { Value = new SplineEasing() }
            });
        }

        property.SetKeyFrameAnimation(animation);
        return animation;
    }
}
