using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneCompositorTests
{
    private static string GetTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"beutl_scene_compositor_{Guid.NewGuid():N}");
    }

    private static Scene CreateScene(string basePath)
    {
        Directory.CreateDirectory(basePath);
        var scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene"))
        };
        return scene;
    }

    private static Element CreateElement(string basePath, bool isEnabled, EngineObject obj)
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = isEnabled,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(obj);
        return element;
    }

    private static TimelineLayer CreateLayer(int zIndex, bool solo = false, bool audioMuted = false, bool videoMuted = false)
    {
        return new TimelineLayer
        {
            ZIndex = zIndex,
            IsSolo = solo,
            IsAudioMuted = audioMuted,
            IsVideoMuted = videoMuted,
        };
    }

    [Test]
    public void EvaluateGraphics_DisabledElement_IsExcluded()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element enabled = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            Element disabled = CreateElement(basePath, isEnabled: false, new TestGraphicsObject());
            scene.Children.Add(enabled);
            scene.Children.Add(disabled);
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(enabled.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateAudio_DisabledElement_IsExcluded()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element enabled = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            Element disabled = CreateElement(basePath, isEnabled: false, new TestAudioObject());
            scene.Children.Add(enabled);
            scene.Children.Add(disabled);
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(enabled.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_TogglingIsEnabled_UpdatesFrameContents()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene);
            var time = TimeSpan.FromMilliseconds(500);

            Assert.That(compositor.EvaluateGraphics(time).Objects.Length, Is.EqualTo(1));

            element.IsEnabled = false;
            Assert.That(compositor.EvaluateGraphics(time).Objects.Length, Is.EqualTo(0));

            element.IsEnabled = true;
            Assert.That(compositor.EvaluateGraphics(time).Objects.Length, Is.EqualTo(1));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateAudio_TogglingIsEnabled_UpdatesFrameContents()
    {
        // Graphics と Audio の両方が同じ SortLayers を経由するため、
        // 有効/無効切り替え時に音声側も一貫してフィルタされることを確認する。
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene);
            var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));

            Assert.That(compositor.EvaluateAudio(range).Objects.Length, Is.EqualTo(1));

            element.IsEnabled = false;
            Assert.That(compositor.EvaluateAudio(range).Objects.Length, Is.EqualTo(0));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_SoloedLayer_OnlySoloedContributes()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element layer0 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            layer0.ZIndex = 0;
            Element layer1 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            layer1.ZIndex = 1;
            scene.Children.Add(layer0);
            scene.Children.Add(layer1);
            scene.Layers.Add(CreateLayer(0));
            scene.Layers.Add(CreateLayer(1, solo: true));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(layer1.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateAudio_MultipleSolos_AreNonExclusive()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element z0 = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            z0.ZIndex = 0;
            Element z1 = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            z1.ZIndex = 1;
            Element z2 = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            z2.ZIndex = 2;
            scene.Children.Add(z0);
            scene.Children.Add(z1);
            scene.Children.Add(z2);
            scene.Layers.Add(CreateLayer(0, solo: true));
            scene.Layers.Add(CreateLayer(1));
            scene.Layers.Add(CreateLayer(2, solo: true));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            Assert.That(frame.Objects.Length, Is.EqualTo(2));
            Assert.That(frame.Objects.Select(o => o.GetOriginal()),
                Is.EquivalentTo(new[] { z0.Objects[0], z2.Objects[0] }));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_Solo_LayerWithoutModelIsExcluded()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element soloed = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            soloed.ZIndex = 0;
            Element modelless = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            modelless.ZIndex = 5;
            scene.Children.Add(soloed);
            scene.Children.Add(modelless);
            scene.Layers.Add(CreateLayer(0, solo: true));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(soloed.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_VideoMutedLayer_ExcludedFromGraphics()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element z0 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            z0.ZIndex = 0;
            Element z1 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            z1.ZIndex = 1;
            scene.Children.Add(z0);
            scene.Children.Add(z1);
            scene.Layers.Add(CreateLayer(0, videoMuted: true));
            scene.Layers.Add(CreateLayer(1));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z1.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateAudio_VideoMutedLayer_StillComposesAudio()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element z0 = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            z0.ZIndex = 0;
            scene.Children.Add(z0);
            scene.Layers.Add(CreateLayer(0, videoMuted: true));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z0.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateAudio_AudioMutedLayer_ExcludedFromAudio()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element z0 = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            z0.ZIndex = 0;
            Element z1 = CreateElement(basePath, isEnabled: true, new TestAudioObject());
            z1.ZIndex = 1;
            scene.Children.Add(z0);
            scene.Children.Add(z1);
            scene.Layers.Add(CreateLayer(0, audioMuted: true));
            scene.Layers.Add(CreateLayer(1));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z1.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_AudioMutedLayer_StillComposesGraphics()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element z0 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            z0.ZIndex = 0;
            scene.Children.Add(z0);
            scene.Layers.Add(CreateLayer(0, audioMuted: true));
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z0.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_LockedElement_StillEvaluated()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            element.IsLocked = true;
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene);

            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(element.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_SoloToggledBetweenEvaluations_IsReflected()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element z0 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            z0.ZIndex = 0;
            Element z1 = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            z1.ZIndex = 1;
            scene.Children.Add(z0);
            scene.Children.Add(z1);
            TimelineLayer layer1 = CreateLayer(1);
            scene.Layers.Add(CreateLayer(0));
            scene.Layers.Add(layer1);
            using var compositor = new SceneCompositor(scene);

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects.Length, Is.EqualTo(2));

            layer1.IsSolo = true;
            CompositionFrame frame = compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(frame.Objects.Length, Is.EqualTo(1));
            Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z1.Objects[0]));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_MuteToggledBetweenEvaluations_IsReflected()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            scene.Children.Add(element);
            TimelineLayer layer = CreateLayer(0);
            scene.Layers.Add(layer);
            using var compositor = new SceneCompositor(scene);

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects.Length, Is.EqualTo(1));

            layer.IsVideoMuted = true;

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects, Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_LayerAddedBetweenEvaluations_IsReflected()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene);

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects.Length, Is.EqualTo(1));

            scene.Layers.Add(CreateLayer(0, videoMuted: true));

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects, Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_LayerZIndexChangedBetweenEvaluations_IsReflected()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, isEnabled: true, new TestGraphicsObject());
            element.ZIndex = 1;
            scene.Children.Add(element);
            TimelineLayer layer = CreateLayer(0, videoMuted: true);
            scene.Layers.Add(layer);
            using var compositor = new SceneCompositor(scene);

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects.Length, Is.EqualTo(1));

            layer.ZIndex = 1;

            Assert.That(compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500)).Objects, Is.Empty);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_CompositorForceOriginalTrue_SeedsPreferProxyFalse()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };

            compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.Multiple(() =>
            {
                Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
                Assert.That(capture.CapturedContexts[0].PreferProxy, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(PreviewSourceMode.PreferProxy, true)]
    [TestCase(PreviewSourceMode.ForceOriginal, false)]
    public void EvaluateGraphics_CompositorForceOriginalFalse_SeedsPreferProxyFromGlobalConfig(
        PreviewSourceMode mode, bool expectedPreferProxy)
    {
        string basePath = GetTempPath();
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = mode;
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            using var compositor = new SceneCompositor(scene);

            compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.Multiple(() =>
            {
                Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
                Assert.That(capture.CapturedContexts[0].PreferProxy, Is.EqualTo(expectedPreferProxy));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = original;
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_PropagatesForceOriginalPreviewIntoReferencedScene()
    {
        string basePath = GetTempPath();
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.ForceOriginal;
            Scene childScene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            childScene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            Scene parentScene = CreateScene(basePath);
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.CurrentValue = childScene;
            parentScene.Children.Add(CreateElement(basePath, isEnabled: true, sceneDrawable));
            using var compositor = new SceneCompositor(parentScene);

            compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.Multiple(() =>
            {
                Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
                Assert.That(capture.CapturedContexts[0].PreferProxy, Is.False);
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = original;
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    // The dangerous export configuration: the compositor forces original decode while the global
    // preview mode prefers proxies. Without SceneDrawable's propagation the nested compositor
    // would seed PreferProxy from the global config and export a referenced scene from proxies.
    [Test]
    public void EvaluateGraphics_ExportForceOriginal_OverridesPreferProxyConfigInReferencedScene()
    {
        string basePath = GetTempPath();
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.PreferProxy;
            Scene childScene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            childScene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            Scene parentScene = CreateScene(basePath);
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.CurrentValue = childScene;
            parentScene.Children.Add(CreateElement(basePath, isEnabled: true, sceneDrawable));
            using var compositor = new SceneCompositor(parentScene) { ForceOriginalSource = true };

            compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.Multiple(() =>
            {
                Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
                Assert.That(capture.CapturedContexts[0].PreferProxy, Is.False);
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = original;
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateAudio_ExportForceOriginal_PropagatesIntoReferencedSceneSound()
    {
        string basePath = GetTempPath();
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.PreferProxy;
            Scene childScene = CreateScene(basePath);
            Scene parentScene = CreateScene(basePath);
            var sceneSound = new SceneSound();
            sceneSound.ReferencedScene.CurrentValue = childScene;
            parentScene.Children.Add(CreateElement(basePath, isEnabled: true, sceneSound));
            using var compositor = new SceneCompositor(parentScene) { ForceOriginalSource = true };

            CompositionFrame frame = compositor.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            var resource = (SceneSound.Resource)frame.Objects[0];
            Assert.Multiple(() =>
            {
                Assert.That(resource._compositor, Is.Not.Null);
                Assert.That(resource._compositor!.ForceOriginalSource, Is.True);
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = original;
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Beutl.Engine.SuppressResourceClassGeneration]
    private class TestGraphicsObject : EngineObject
    {
        public override CompositionTarget GetCompositionTarget() => CompositionTarget.Graphics;
    }

    [Beutl.Engine.SuppressResourceClassGeneration]
    private class TestAudioObject : EngineObject
    {
        public override CompositionTarget GetCompositionTarget() => CompositionTarget.Audio;
    }
}

internal sealed partial class SceneCompositorContextCaptureDrawable : Drawable
{
    public List<CapturedCompositionContext> CapturedContexts { get; } = [];

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        partial void PostUpdate(SceneCompositorContextCaptureDrawable obj, CompositionContext context)
        {
            obj.CapturedContexts.Add(new CapturedCompositionContext(
                context.PreferProxy));
        }
    }
}

internal readonly record struct CapturedCompositionContext(
    bool PreferProxy);
