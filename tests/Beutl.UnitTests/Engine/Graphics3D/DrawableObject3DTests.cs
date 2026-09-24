using System.Numerics;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public class DrawableObject3DTests
{
    private static RectShape CreateRect(float x, float y)
    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 400;
        rect.Height.CurrentValue = 250;
        rect.Fill.CurrentValue = Brushes.Red;
        rect.Transform.CurrentValue = new TranslateTransform(x, y);
        return rect;
    }

    [Test]
    public void TakesTheDrawablesBeforeItInTheFlow_AndReachesTheSceneAsAnObject()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"beutl_drawable_object3d_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        try
        {
            var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(basePath, "test.scene")) };
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(basePath, "element.layer"))
            };
            var rect = CreateRect(0, 0);
            var card = new DrawableObject3D();
            var scene3D = new Scene3D();
            element.AddObject(rect);
            element.AddObject(card);
            element.AddObject(scene3D);
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.Zero);

            // Element.AddObject puts a portal in front of each flow operator; it draws nothing.
            Assert.That(
                frame.Objects.Select(o => o.GetOriginal()).Where(o => o is not PortalObject),
                Is.EqualTo(new EngineObject?[] { scene3D }));
            var sceneResource = frame.Objects.OfType<Scene3D.Resource>().Single();
            var cardResource = sceneResource.Objects.OfType<DrawableObject3D.Resource>().Single();
            Assert.That(cardResource.Children.Select(c => c.GetOriginal()), Is.EqualTo(new[] { rect }));
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void DefaultsToAnUnlitMaterialThatCastsNoShadow()
    {
        var card = new DrawableObject3D();

        Assert.That(card.Material.CurrentValue, Is.InstanceOf<UnlitMaterial>());
        Assert.That(card.CastShadows.CurrentValue, Is.False);
    }

    // The card covers where the drawables sit on a 2D canvas the size of the scene and keeps that place as an
    // offset from its position, so Position (0, 0, 0) shows it where 2D would.
    [Test]
    public void Layout_PlacesTheCardWhereTheDrawablesSitInTwoDimensions()
    {
        var card = new DrawableObject3D();
        card.Children.Add(CreateRect(400, 150));
        using var resource = (DrawableObject3D.Resource)card.ToResource(CompositionContext.Default);

        resource.UpdateLayout(new Size(1920, 1080), density: 1);

        // The rect covers (1160, 565, 400, 250); the card keeps a 2 px margin for anti-aliasing.
        Assert.That(resource.Content.ContentBounds, Is.EqualTo(new Rect(1158, 563, 404, 254)));
        Assert.That(resource.ContentOffset, Is.EqualTo(new Vector3(400, 150, 0)));
        Assert.That(resource.GetWorldMatrix().Translation, Is.EqualTo(new Vector3(400, 150, 0)));
        Vector3 corner = Vector3.Transform(new Vector3(-0.5f, 0, 0.5f), resource.ContentMatrix);
        Assert.That(corner.X, Is.EqualTo(-202).Within(1e-3f));
        Assert.That(corner.Y, Is.EqualTo(-127).Within(1e-3f));
        Assert.That(corner.Z, Is.EqualTo(0).Within(1e-3f));
    }

    [Test]
    public void Layout_WithoutDrawables_HasNoMesh()
    {
        var card = new DrawableObject3D();
        using var resource = (DrawableObject3D.Resource)card.ToResource(CompositionContext.Default);

        resource.UpdateLayout(new Size(1920, 1080), density: 1);

        Assert.That(resource.GetMesh(), Is.Null);
    }
}
