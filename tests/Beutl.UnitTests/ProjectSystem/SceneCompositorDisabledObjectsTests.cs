using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class SceneCompositorDisabledObjectsTests
{
    // The curves tab reads the colors a Curves effect receives from a frame rendered without it. Only that frame's
    // compositor leaves the effect out: the preview's evaluates it as before, and the effect itself stays on.
    [Test]
    public void A_compositor_evaluates_the_objects_it_disables_as_turned_off()
    {
        var curves = new Curves();
        var blur = new Blur();
        var shape = new RectShape { Width = { Value = 8 }, Height = { Value = 8 } };
        var group = (FilterEffectGroup)shape.FilterEffect.Value!;
        group.Children.Add(curves);
        group.Children.Add(blur);
        var scene = new Scene(16, 16, string.Empty);
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.Objects.Add(shape);
        scene.Children.Add(element);

        using var preview = new SceneCompositor(scene, TestFonts.RenderServices);
        using var sampling = new SceneCompositor(scene, TestFonts.RenderServices) { DisabledObjects = new HashSet<EngineObject> { curves } };

        Assert.Multiple(() =>
        {
            Assert.That(EnabledEffects(preview), Is.EqualTo(new[] { true, true }));
            Assert.That(EnabledEffects(sampling), Is.EqualTo(new[] { false, true }));
            Assert.That(EnabledEffects(preview), Is.EqualTo(new[] { true, true }), "Evaluating without it changes nothing else.");
            Assert.That(curves.IsEnabled.Value, Is.True);
        });
    }

    // The curves tab reads the colors a Curves effect receives from a frame where the effects after it change no colors,
    // while an effect that moves pixels still puts them where the preview shows them.
    [Test]
    public void Effects_after_a_picked_one_keep_their_moves_and_change_no_colors()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var curves = new Curves();
        var turn = new HueRotate { Angle = { Value = 180 } };
        var move = new TransformEffect { Transform = { Value = new TranslateTransform(16, 0) } };
        var shape = new RectShape
        {
            Width = { Value = 32 },
            Height = { Value = 32 },
            Fill = { Value = new SolidColorBrush(Colors.Red) },
        };
        var chain = (FilterEffectGroup)shape.FilterEffect.Value!;
        chain.Children.Add(curves);
        chain.Children.Add(turn);
        chain.Children.Add(move);
        var scene = new Scene(32, 32, string.Empty);
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.Objects.Add(shape);
        scene.Children.Add(element);

        Bgra8888[] preview = MiddleRow(scene, _ => { });
        Bgra8888[] picked = MiddleRow(scene, compositor =>
        {
            compositor.DisabledObjects = new HashSet<EngineObject> { curves };
            compositor.ColorStagesSkipped = FilterEffectOrder.After(curves);
        });

        Assert.Multiple(() =>
        {
            Assert.That(preview[24].R, Is.LessThan(64), "The preview turns the red away.");
            Assert.That(picked[24].R, Is.GreaterThan(250), "The picked frame keeps the red.");
            Assert.That(Math.Max(picked[24].G, picked[24].B), Is.LessThan(5));
            Assert.That((preview[8].A, picked[8].A), Is.EqualTo(((byte)0, (byte)0)), "Both move the rect off the left half.");
        });
    }

    // A group draws each of its children as an effect of its own, so a color effect in a later group, or in the effects
    // of a drawable group around the drawable, changes no colors in the picked frame either.
    [Test]
    public void Effects_in_groups_after_a_picked_one_change_no_colors()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var curves = new Curves();
        var shape = new RectShape
        {
            Width = { Value = 32 },
            Height = { Value = 32 },
            Fill = { Value = new SolidColorBrush(Colors.Red) },
        };
        var chain = (FilterEffectGroup)shape.FilterEffect.Value!;
        chain.Children.Add(curves);
        var later = new FilterEffectGroup();
        later.Children.Add(new HueRotate { Angle = { Value = 90 } });
        chain.Children.Add(later);
        var group = new DrawableGroup();
        group.Children.Add(shape);
        ((FilterEffectGroup)group.FilterEffect.Value!).Children.Add(new HueRotate { Angle = { Value = 90 } });
        var scene = new Scene(32, 32, string.Empty);
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.Objects.Add(group);
        scene.Children.Add(element);

        Bgra8888[] preview = MiddleRow(scene, _ => { });
        Bgra8888[] picked = MiddleRow(scene, compositor =>
        {
            compositor.DisabledObjects = new HashSet<EngineObject> { curves };
            compositor.ColorStagesSkipped = FilterEffectOrder.After(curves);
        });

        Assert.Multiple(() =>
        {
            Assert.That(preview[16].R, Is.LessThan(64), "The preview turns the red away.");
            Assert.That(picked[16].R, Is.GreaterThan(250), "The picked frame keeps the red.");
            Assert.That(Math.Max(picked[16].G, picked[16].B), Is.LessThan(5));
        });
    }

    private static Bgra8888[] MiddleRow(Scene scene, Action<SceneCompositor> configure)
        => VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new SceneRenderer(scene, TestFonts.RenderServices, RenderIntent.Preview);
            configure(renderer.Compositor);
            renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.Zero));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap pixels = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            return pixels.GetRow<Bgra8888>(16).ToArray();
        });

    private static bool[] EnabledEffects(SceneCompositor compositor)
    {
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        Drawable.Resource drawable = frame.Objects.OfType<Drawable.Resource>().Single();
        return [.. ((FilterEffectGroup.Resource)drawable.FilterEffect!).Children.Select(effect => effect.IsEnabled)];
    }
}
