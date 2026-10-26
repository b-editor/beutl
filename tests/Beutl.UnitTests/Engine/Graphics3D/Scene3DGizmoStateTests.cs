using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

// Camera mode shows the gizmo by writing Scene3D.GizmoTarget and GizmoMode. They are the editor's state, not the
// scene's: they stay out of the saved scene and out of history, and only a preview draws them.
[TestFixture]
[NonParallelizable]
public sealed class Scene3DGizmoStateTests
{
    [Test]
    public void TheGizmoSelection_IsNotSaved()
    {
        var scene = new Scene3D();
        scene.GizmoTarget.Value = Guid.NewGuid();
        scene.GizmoMode.Value = GizmoMode.Rotate;
        scene.AmbientIntensity.Value = 0.5f;

        JsonObject json = CoreSerializer.SerializeToJsonObject(scene);

        Assert.Multiple(() =>
        {
            Assert.That(json.ContainsKey(nameof(Scene3D.GizmoTarget)), Is.False);
            Assert.That(json.ContainsKey(nameof(Scene3D.GizmoMode)), Is.False);
            Assert.That(json.ContainsKey(nameof(Scene3D.AmbientIntensity)), Is.True,
                "the control: the scene's own properties are still saved");
        });
    }

    // Scenes saved while a gizmo was shown carry the selection, and restoring it would draw the gizmo again
    // with camera mode off.
    [Test]
    public void AGizmoSelectionInASavedScene_IsNotRestored()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Scene3D());
        json[nameof(Scene3D.GizmoTarget)] = JsonSerializer.SerializeToNode<Guid?>(
            Guid.NewGuid(), JsonHelper.SerializerOptions);
        json[nameof(Scene3D.GizmoMode)] = JsonSerializer.SerializeToNode(GizmoMode.Rotate, JsonHelper.SerializerOptions);

        var restored = (Scene3D)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene3D));

        Assert.Multiple(() =>
        {
            Assert.That(restored.GizmoTarget.Value, Is.Null);
            Assert.That(restored.GizmoMode.Value, Is.EqualTo(GizmoMode.None));
        });
    }

    [Test]
    public void SelectingAndClearingTheGizmo_RecordsNoHistory()
    {
        var scene = new Scene3D();
        using var harness = new HistoryHarness(scene);

        scene.GizmoTarget.Value = Guid.NewGuid();
        scene.GizmoMode.Value = GizmoMode.Translate;
        harness.History.Commit();
        scene.GizmoTarget.Value = null;
        scene.GizmoMode.Value = GizmoMode.None;
        harness.History.Commit();

        Assert.That(harness.History.UndoCount, Is.Zero);

        scene.AmbientIntensity.Value = 0.5f;
        harness.History.Commit();

        Assert.That(harness.History.UndoCount, Is.EqualTo(1), "the control: an edit to the scene is still recorded");
    }

    // The Output tab, Save frame and the AI dialog's current frame render the edited scene through
    // ExportRendererFactory, so they would show whatever camera mode has selected.
    [Test]
    public void AnExport_DrawsNoGizmo_WhileThePreviewDoes()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var cube = new Cube3D();
            var scene3D = new Scene3D();
            scene3D.RenderWidth.Value = 160;
            scene3D.RenderHeight.Value = 120;
            scene3D.Objects.Add(cube);
            var element = new Element
            {
                Start = { Value = MediaTime.Zero },
                Length = { Value = MediaTime.FromSeconds(1) },
                IsEnabled = { Value = true },
            };
            element.AddObject(scene3D);
            var scene = new Scene(160, 120, string.Empty);
            scene.Children.Add(element);

            string previewWithout = RenderAndHash(new SceneRenderer(scene, RenderIntent.Preview));
            string exportWithout = RenderAndHash(ExportRendererFactory.Create(scene));

            scene3D.GizmoTarget.Value = cube.Id.Value;
            scene3D.GizmoMode.Value = GizmoMode.Translate;
            string previewWith = RenderAndHash(new SceneRenderer(scene, RenderIntent.Preview));
            string exportWith = RenderAndHash(ExportRendererFactory.Create(scene));

            Assert.Multiple(() =>
            {
                Assert.That(previewWith, Is.Not.EqualTo(previewWithout),
                    "the control: the preview draws the gizmo, so a frame with it can be told apart");
                Assert.That(exportWith, Is.EqualTo(exportWithout), "an export must not carry the gizmo");
            });
        });
    }

    [Test]
    public void Presentation_KeepsPreviewAllocationFailureBehavior()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var device = new Mock<IGraphicsContext>();
            device.SetupGet(c => c.Supports3DRendering).Returns(true);
            device.SetupGet(c => c.MaxAttachmentDimension).Returns(8192);
            device.SetupGet(c => c.MaxCubeFaceDimension).Returns(8192);
            device.SetupGet(c => c.SkiaContext).Returns(GraphicsContextFactory.SharedContext!.SkiaContext);
            device.Setup(c => c.CreateShaderCompiler()).Returns(Mock.Of<IShaderCompiler>());
            device.Setup(c => c.CreateTextureArray(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<TextureFormat>()))
                .Throws(new InvalidOperationException("Simulated 3D allocation failure."));
            InstalledGraphics previous = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(device.Object, null, null, FailedToInitialize: false));
            // Keep the 2D target available while the 3D device refuses its allocation.
            var allocationContext = RenderTarget.ExchangeAllocationContext(() => null);
            try
            {
                var scene = new Scene(160, 120, string.Empty);
                var threeD = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
                threeD.AddObject(new Scene3D
                {
                    RenderWidth = { Value = 160 },
                    RenderHeight = { Value = 120 },
                    BackgroundColor = { Value = Colors.Blue },
                });
                scene.Children.Add(threeD);
                var twoD = new Element { Length = { Value = MediaTime.FromSeconds(1) }, ZIndex = { Value = 1 } };
                twoD.AddObject(new RectShape
                {
                    Width = { Value = 60 },
                    Height = { Value = 50 },
                    Fill = { Value = new SolidColorBrush(Colors.Red) },
                });
                scene.Children.Add(twoD);
                using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
                renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.Zero));
                using Bitmap preview = renderer.Snapshot();
                using Bitmap clean = renderer.SnapshotWithoutGizmos()!;
                Assert.That(clean, Is.Not.Null);
                Assert.That(clean.GetPixelSpan().ToArray(), Is.EqualTo(preview.GetPixelSpan().ToArray()));
                Assert.That(clean.GetPixelSpan().ToArray(), Has.Some.Not.Zero, "The 2D content must still be drawn.");
                device.Verify(c => c.CreateTextureArray(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<TextureFormat>()),
                    Times.AtLeastOnce, "The control must exercise a real 3D allocation failure.");
            }
            finally
            {
                RenderTarget.ExchangeAllocationContext(allocationContext);
                GraphicsContextFactory.ExchangeInstalledGraphics(previous);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Presentation_OnlyReplays3DFrames_WhenSwitchingBetweenRenderedAndCachedFrames(bool embedded)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            static void Add(Scene scene, Drawable drawable, int start)
            {
                var element = new Element
                {
                    Start = { Value = MediaTime.FromSeconds(start) },
                    Length = { Value = MediaTime.FromSeconds(1) },
                };
                element.AddObject(drawable);
                scene.Children.Add(element);
            }

            var nested = new Scene(160, 120, string.Empty);
            var scene3D = new Scene3D { RenderWidth = { Value = 160 }, RenderHeight = { Value = 120 } };
            if (embedded) Add(nested, scene3D, 0);
            var scene = new Scene(160, 120, string.Empty);
            Add(scene, new RectShape
            {
                Width = { Value = 160 },
                Height = { Value = 120 },
                Fill = { Value = new DrawableBrush(embedded ? new SceneDrawable { ReferencedScene = { Value = nested } } : scene3D) },
            }, 0);
            Add(scene, new RectShape
            {
                Width = { Value = 60 },
                Height = { Value = 50 },
                Fill = { Value = new SolidColorBrush(Colors.Red) },
                FilterEffect = { Value = new Blur { Sigma = { Value = new Size(2, 2) } } },
            }, 1);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview) { CacheOptions = RenderCacheOptions.Enabled };
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (int second in new[] { 0, 1 })
                {
                    var frame = renderer.Compositor.EvaluateGraphics(MediaTime.FromSeconds(second));
                    if (pass == 0) renderer.Render(frame);
                    else renderer.UpdateFrame(frame); // The frame-cache hit path only evaluates resources.
                    using Bitmap? clean = renderer.SnapshotWithoutGizmos();
                    Assert.That(clean is not null, Is.EqualTo(second == 0),
                        "Only 3D content needs a separate image, including when returning from another cached frame.");
                }
            }
        });
    }

    private static string RenderAndHash(SceneRenderer renderer)
    {
        using (renderer)
        {
            renderer.Render(renderer.Compositor.EvaluateGraphics(MediaTime.Zero));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(
                BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            return Convert.ToHexString(SHA256.HashData(srgb.GetPixelSpan()));
        }
    }
}
