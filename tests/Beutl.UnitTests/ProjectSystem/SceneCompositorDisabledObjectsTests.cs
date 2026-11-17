using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

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

        using var preview = new SceneCompositor(scene);
        using var sampling = new SceneCompositor(scene) { DisabledObjects = new HashSet<EngineObject> { curves } };

        Assert.Multiple(() =>
        {
            Assert.That(EnabledEffects(preview), Is.EqualTo(new[] { true, true }));
            Assert.That(EnabledEffects(sampling), Is.EqualTo(new[] { false, true }));
            Assert.That(EnabledEffects(preview), Is.EqualTo(new[] { true, true }), "Evaluating without it changes nothing else.");
            Assert.That(curves.IsEnabled.Value, Is.True);
        });
    }

    private static bool[] EnabledEffects(SceneCompositor compositor)
    {
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        Drawable.Resource drawable = frame.Objects.OfType<Drawable.Resource>().Single();
        return [.. ((FilterEffectGroup.Resource)drawable.FilterEffect!).Children.Select(effect => effect.IsEnabled)];
    }
}
