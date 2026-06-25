using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.E2ETests.Scenarios;

[TestFixture]
public class AnimationRoundTripTests
{
    private string _baseDir = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"beutl-e2e-anim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true);
    }

    private static KeyFrameAnimation<float> BuildOpacityAnimation()
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 0f,
            Easing = new LinearEasing(),
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(2),
            Value = 100f,
            Easing = new SplineEasing(0.1f, 0.2f, 0.3f, 0.4f),
        });
        return animation;
    }

    [Test]
    public void Animated_drawable_property_round_trips_through_element_on_disk()
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(_baseDir, "animated.belm")),
        };
        var shape = new RectShape();
        shape.Opacity.Animation = BuildOpacityAnimation();
        element.Objects.Add(shape);

        CoreSerializer.StoreToUri(element, element.Uri!);
        var restored = CoreSerializer.RestoreFromUri<Element>(element.Uri!);

        RectShape restoredShape = restored.Objects.OfType<RectShape>().Single();
        var restoredAnimation = restoredShape.Opacity.Animation as KeyFrameAnimation<float>;

        Assert.That(restoredAnimation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(restoredAnimation!.UseGlobalClock, Is.True);
            Assert.That(restoredAnimation.KeyFrames, Has.Count.EqualTo(2));
        });

        var first = (KeyFrame<float>)restoredAnimation!.KeyFrames[0];
        var second = (KeyFrame<float>)restoredAnimation.KeyFrames[1];

        Assert.Multiple(() =>
        {
            Assert.That(first.KeyTime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(first.Value, Is.EqualTo(0f));
            Assert.That(first.Easing, Is.InstanceOf<LinearEasing>());

            Assert.That(second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(second.Value, Is.EqualTo(100f));
        });

        var spline = second.Easing as SplineEasing;
        Assert.That(spline, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(spline!.X1, Is.EqualTo(0.1f));
            Assert.That(spline.Y1, Is.EqualTo(0.2f));
            Assert.That(spline.X2, Is.EqualTo(0.3f));
            Assert.That(spline.Y2, Is.EqualTo(0.4f));
        });
    }

    [Test]
    public void KeyFrameAnimation_round_trips_through_json_object()
    {
        KeyFrameAnimation<float> animation = BuildOpacityAnimation();

        JsonObject json = CoreSerializer.SerializeToJsonObject(animation);
        var restored = (KeyFrameAnimation<float>)CoreSerializer.DeserializeFromJsonObject(json, typeof(KeyFrameAnimation<float>));

        Assert.That(restored.KeyFrames, Has.Count.EqualTo(2));
        var first = (KeyFrame<float>)restored.KeyFrames[0];
        var second = (KeyFrame<float>)restored.KeyFrames[1];

        Assert.Multiple(() =>
        {
            Assert.That(first.Value, Is.EqualTo(0f));
            Assert.That(first.Easing, Is.InstanceOf<LinearEasing>());
            Assert.That(second.Value, Is.EqualTo(100f));
            Assert.That(second.Easing, Is.InstanceOf<SplineEasing>());
            Assert.That(restored.UseGlobalClock, Is.True);
        });
    }

    [Test]
    public void Animation_interpolates_endpoint_values_after_round_trip()
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(_baseDir, "interp.belm")),
        };
        var shape = new RectShape();
        shape.Opacity.Animation = BuildOpacityAnimation();
        element.Objects.Add(shape);

        CoreSerializer.StoreToUri(element, element.Uri!);
        var restored = CoreSerializer.RestoreFromUri<Element>(element.Uri!);
        var restoredAnimation = (KeyFrameAnimation<float>)restored.Objects.OfType<RectShape>().Single().Opacity.Animation!;

        Assert.Multiple(() =>
        {
            Assert.That(restoredAnimation.Interpolate(TimeSpan.Zero), Is.EqualTo(0f));
            Assert.That(restoredAnimation.Interpolate(TimeSpan.FromSeconds(2)), Is.EqualTo(100f));
        });
    }
}
