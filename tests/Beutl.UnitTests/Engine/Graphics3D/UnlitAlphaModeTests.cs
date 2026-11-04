using Beutl.Composition;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public class UnlitAlphaModeTests
{
    private static Object3D.Resource Mesh(UnlitMaterial material)
    {
        var cube = new Cube3D();
        cube.Material.Value = material;
        return (Object3D.Resource)cube.ToResource(CompositionContext.Default);
    }

    [TestCase(AlphaMode.Opaque, 1f, true)]
    [TestCase(AlphaMode.Opaque, 0.5f, true)]
    [TestCase(AlphaMode.Mask, 0.75f, true)]
    [TestCase(AlphaMode.Blend, 1f, false)]
    [TestCase(AlphaMode.Blend, 0.5f, false)]
    public void A_chosen_mode_decides_whether_a_mesh_writes_depth(AlphaMode mode, float opacity, bool expected)
    {
        using Object3D.Resource mesh = Mesh(new UnlitMaterial { AlphaMode = { Value = mode }, Opacity = { Value = opacity } });

        Assert.That(mesh.Material!.WritesDepth, Is.EqualTo(expected));
    }

    // What the material did before the modes existed, which is what a saved scene expects.
    [TestCase(1f, 255, true)]
    [TestCase(0.5f, 255, false)]
    [TestCase(1f, 128, false)]
    public void Auto_writes_depth_for_a_mesh_with_an_opaque_tint(float opacity, int alpha, bool expected)
    {
        using Object3D.Resource mesh = Mesh(new UnlitMaterial
        {
            Opacity = { Value = opacity },
            Color = { Value = new Color((byte)alpha, 255, 255, 255) },
        });

        Assert.That(mesh.Material!.WritesDepth, Is.EqualTo(expected));
    }

    [Test]
    public void Auto_is_the_default_and_a_card_writes_no_depth()
    {
        var card = new DrawableObject3D();
        Assert.That(((UnlitMaterial)card.Material.Value!).AlphaMode.Value, Is.EqualTo(AlphaMode.Auto));

        using var resource = (Object3D.Resource)card.ToResource(CompositionContext.Default);
        Assert.Multiple(() =>
        {
            Assert.That(resource.Material!.WritesDepth, Is.False);
            Assert.That(resource.Material!.IsDoubleSided, Is.True);
        });
    }

    [Test]
    public void A_card_set_to_mask_shows_both_sides_and_writes_depth()
    {
        var card = new DrawableObject3D();
        ((UnlitMaterial)card.Material.Value!).AlphaMode.Value = AlphaMode.Mask;

        using var resource = (Object3D.Resource)card.ToResource(CompositionContext.Default);
        Assert.Multiple(() =>
        {
            Assert.That(resource.Material!.WritesDepth, Is.True);
            Assert.That(resource.Material!.IsDoubleSided, Is.True);
        });
    }

    [TestCase(AlphaMode.Auto, 0f, true)]
    [TestCase(AlphaMode.Blend, 0f, true)]
    [TestCase(AlphaMode.Blend, 0.01f, false)]
    [TestCase(AlphaMode.Opaque, 0f, false)]
    [TestCase(AlphaMode.Mask, 0.4f, true)]
    [TestCase(AlphaMode.Mask, 0.5f, false)]
    public void A_material_that_keeps_no_pixels_is_invisible(AlphaMode mode, float opacity, bool expected)
    {
        using Object3D.Resource mesh = Mesh(new UnlitMaterial { AlphaMode = { Value = mode }, Opacity = { Value = opacity } });

        Assert.That(mesh.Material!.IsInvisible, Is.EqualTo(expected));
    }
}
