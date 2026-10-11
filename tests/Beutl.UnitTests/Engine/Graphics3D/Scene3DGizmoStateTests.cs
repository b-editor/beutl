using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;

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
        scene.GizmoTarget.CurrentValue = Guid.NewGuid();
        scene.GizmoMode.CurrentValue = GizmoMode.Rotate;
        scene.AmbientIntensity.CurrentValue = 0.5f;

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
            Assert.That(restored.GizmoTarget.CurrentValue, Is.Null);
            Assert.That(restored.GizmoMode.CurrentValue, Is.EqualTo(GizmoMode.None));
        });
    }

    [Test]
    public void SelectingAndClearingTheGizmo_RecordsNoHistory()
    {
        var scene = new Scene3D();
        using var harness = new HistoryHarness(scene);

        scene.GizmoTarget.CurrentValue = Guid.NewGuid();
        scene.GizmoMode.CurrentValue = GizmoMode.Translate;
        harness.History.Commit();
        scene.GizmoTarget.CurrentValue = null;
        scene.GizmoMode.CurrentValue = GizmoMode.None;
        harness.History.Commit();

        Assert.That(harness.History.UndoCount, Is.Zero);

        scene.AmbientIntensity.CurrentValue = 0.5f;
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
            scene3D.RenderWidth.CurrentValue = 160;
            scene3D.RenderHeight.CurrentValue = 120;
            scene3D.Objects.Add(cube);
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
            };
            element.AddObject(scene3D);
            var scene = new Scene(160, 120, string.Empty);
            scene.Children.Add(element);

            string previewWithout = RenderAndHash(new SceneRenderer(scene, RenderIntent.Preview));
            string exportWithout = RenderAndHash(ExportRendererFactory.Create(scene));

            scene3D.GizmoTarget.CurrentValue = cube.Id;
            scene3D.GizmoMode.CurrentValue = GizmoMode.Translate;
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

    private static string RenderAndHash(SceneRenderer renderer)
    {
        using (renderer)
        {
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.Zero));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(
                BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            return Convert.ToHexString(SHA256.HashData(srgb.GetPixelSpan()));
        }
    }
}
