using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class AnimationTimeMappingTests
{
    [TestCase(0)]
    [TestCase(2)]
    public void Local_mapping_uses_the_owners_anchor_and_matches_evaluation(int origin)
    {
        var scene = new Scene();
        var project = new Project();
        new TestHierarchicalRoot().HierarchicalChildren.Add(project);
        project.Items.Add(scene);
        var element = new Element { Start = { Value = MediaTime.FromSeconds(5) } };
        var shape = new RectShape
        {
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(origin), MediaTime.FromSeconds(10)) }
        };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = 200 } });
        shape.Width.SetKeyFrameAnimation(animation);
        var group = new DrawableGroup();
        group.Children.Add(shape);
        element.AddObject(group);
        scene.Children.Add(element);
        IKeyFrameAnimation untyped = animation;
        MediaTime local = MediaTime.FromSeconds(1.5) + new MediaTime(1);
        MediaTime at = local + MediaTime.FromSeconds(origin);

        Assert.Multiple(() =>
        {
            Assert.That(untyped.ToLocal(at), Is.EqualTo(local));
            Assert.That(untyped.ToScene(local), Is.EqualTo(at));
            Assert.That(animation.GetAnimatedValue(at), Is.EqualTo(animation.Interpolate(local)));
            Assert.That(animation.ToScene(animation.ToLocal(new MediaTime(-1))), Is.EqualTo(new MediaTime(-1)));
        });

        element.Start.Value = MediaTime.FromSeconds(8);
        Assert.That(animation.ToScene(local), Is.EqualTo(at), "An explicit anchor does not follow the element.");
        shape.IsTimeAnchor.Value = false;
        Assert.That(animation.ToScene(local), Is.EqualTo(local + element.Start.Value));
        element.Start.Value = MediaTime.FromSeconds(9);
        Assert.That(animation.ToLocal(local + element.Start.Value), Is.EqualTo(local));
        animation.UseGlobalClock.Value = true;
        Assert.That(animation.ToScene(local), Is.EqualTo(local));
        Assert.That(animation.ToLocal(at), Is.EqualTo(at));
    }

    [Test]
    public void Detaching_and_reattaching_an_animation_updates_its_origin()
    {
        var project = new Project();
        new TestHierarchicalRoot().HierarchicalChildren.Add(project);
        var scene = new Scene();
        project.Items.Add(scene);
        var first = new RectShape();
        var second = new RectShape();
        var firstElement = new Element { Start = { Value = MediaTime.FromSeconds(2) } };
        var secondElement = new Element { Start = { Value = MediaTime.FromSeconds(7) } };
        firstElement.AddObject(first);
        secondElement.AddObject(second);
        scene.Children.Add(firstElement);
        scene.Children.Add(secondElement);
        var animation = new KeyFrameAnimation<float>();
        MediaTime local = new(1);
        Assert.That(animation.ToScene(local), Is.EqualTo(local));
        first.Width.SetKeyFrameAnimation(animation);
        Assert.That(animation.ToScene(local), Is.EqualTo(first.Start + local));
        first.Width.SetKeyFrameAnimation(null);
        Assert.That(animation.ToScene(local), Is.EqualTo(local));
        second.Width.SetKeyFrameAnimation(animation);
        Assert.That(animation.ToLocal(second.Start + local), Is.EqualTo(local));
    }

    [Test]
    public void Scene_window_mapping_is_exact_unclamped_and_tracks_start()
    {
        var scene = new Scene { Start = { Value = MediaTime.FromSeconds(5) }, Duration = { Value = MediaTime.FromSeconds(2) } };
        foreach (MediaTime time in new[] { new MediaTime(-1), MediaTime.Zero, scene.Duration.Value + new MediaTime(1) })
        {
            Assert.That(scene.FromWindowTime(time), Is.EqualTo(time + scene.Start.Value));
            Assert.That(scene.ToWindowTime(scene.FromWindowTime(time)), Is.EqualTo(time));
        }
        scene.Start.Value = MediaTime.FromSeconds(10);
        Assert.That(scene.ToWindowTime(MediaTime.FromSeconds(5)), Is.EqualTo(MediaTime.FromSeconds(-5)));
    }
}
