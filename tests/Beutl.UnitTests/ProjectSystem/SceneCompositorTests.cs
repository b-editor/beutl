using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneCompositorTests
{
    public enum BusyDescendantCleanupTrigger
    {
        Detach,
        CallbackFailure,
        CompositorDispose,
    }

    [Test]
    public void ICompositor_PurposeOverloadsKeepLegacyFrameImplementationsHonest()
    {
        ICompositor compositor = new LegacyFrameOnlyCompositor();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => compositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame));
            Assert.DoesNotThrow(() => compositor.EvaluateAudio(default, RenderPullPurpose.Frame));
            Assert.Throws<NotSupportedException>(() =>
                compositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary));
            Assert.Throws<NotSupportedException>(() =>
                compositor.EvaluateAudio(default, RenderPullPurpose.Auxiliary));
        });
    }

    [Test]
    public void PurposeAwareEvaluation_AfterDisposeThrowsEvenForEmptyScene()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            var compositor = new SceneCompositor(scene, RenderIntent.Preview);
            compositor.Dispose();

            Assert.Multiple(() =>
            {
                Assert.Throws<ObjectDisposedException>(() =>
                    compositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame));
                Assert.Throws<ObjectDisposedException>(() =>
                    compositor.EvaluateAudio(default, RenderPullPurpose.Auxiliary));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    private static string GetTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"beutl_scene_compositor_{Guid.NewGuid():N}");
    }

    private static Exception? CaptureTaskFailure(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
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

    [TestCase(RenderIntent.Preview, RenderPullPurpose.Frame)]
    [TestCase(RenderIntent.Preview, RenderPullPurpose.Auxiliary)]
    [TestCase(RenderIntent.Delivery, RenderPullPurpose.Frame)]
    [TestCase(RenderIntent.Delivery, RenderPullPurpose.Auxiliary)]
    public void EvaluateGraphics_PropagatesRootRenderPolicy(
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            using var compositor = new SceneCompositor(scene, renderIntent);

            CompositionFrame frame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(500),
                pullPurpose);

            Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
            Assert.That(capture.CapturedContexts[0].RenderIntent, Is.EqualTo(renderIntent));
            Assert.That(capture.CapturedContexts[0].PullPurpose, Is.EqualTo(pullPurpose));
            Assert.That(frame.RenderIntent, Is.EqualTo(renderIntent));
            Assert.That(frame.PullPurpose, Is.EqualTo(pullPurpose));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_ExplicitAuxiliaryPurposeDoesNotChangeOneArgumentFrameDefault()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            using var compositor = new SceneCompositor(scene, RenderIntent.Preview);

            compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(500),
                RenderPullPurpose.Auxiliary);
            compositor.EvaluateGraphics(TimeSpan.FromMilliseconds(500));

            Assert.That(capture.CapturedContexts, Has.Count.EqualTo(2));
            Assert.That(capture.CapturedContexts[0].PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(capture.CapturedContexts[1].PullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_KeepsFrameAndAuxiliaryResourcesIsolated()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            using var compositor = new SceneCompositor(scene, RenderIntent.Preview);

            CompositionFrame firstFrame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(100),
                RenderPullPurpose.Frame);
            var frameResource = (SceneCompositorContextCaptureDrawable.Resource)firstFrame.Objects[0];

            CompositionFrame auxiliaryFrame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(200),
                RenderPullPurpose.Auxiliary);
            var auxiliaryResource = (SceneCompositorContextCaptureDrawable.Resource)auxiliaryFrame.Objects[0];

            Assert.Multiple(() =>
            {
                Assert.That(auxiliaryResource, Is.Not.SameAs(frameResource));
                Assert.That(frameResource.UpdateCount, Is.EqualTo(1));
                Assert.That(frameResource.LastTime, Is.EqualTo(TimeSpan.FromMilliseconds(100)));
                Assert.That(frameResource.LastPullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
                Assert.That(auxiliaryResource.UpdateCount, Is.EqualTo(1));
                Assert.That(auxiliaryResource.LastTime, Is.EqualTo(TimeSpan.FromMilliseconds(200)));
                Assert.That(auxiliaryResource.LastPullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            });

            CompositionFrame secondFrame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(300),
                RenderPullPurpose.Frame);
            CompositionFrame secondAuxiliaryFrame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(400),
                RenderPullPurpose.Auxiliary);

            Assert.Multiple(() =>
            {
                Assert.That(secondFrame.Objects[0], Is.SameAs(frameResource));
                Assert.That(secondAuxiliaryFrame.Objects[0], Is.SameAs(auxiliaryResource));
                Assert.That(frameResource.UpdateCount, Is.EqualTo(2));
                Assert.That(frameResource.LastTime, Is.EqualTo(TimeSpan.FromMilliseconds(300)));
                Assert.That(frameResource.LastPullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
                Assert.That(auxiliaryResource.UpdateCount, Is.EqualTo(2));
                Assert.That(auxiliaryResource.LastTime, Is.EqualTo(TimeSpan.FromMilliseconds(400)));
                Assert.That(auxiliaryResource.LastPullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            });
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PurposeResourceCaches_CleanupBothResourcesOnce_WhenOneDisposeThrows(bool detach)
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            Element element = CreateElement(basePath, isEnabled: true, capture);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            var frameResource = (SceneCompositorContextCaptureDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame)
                .Objects[0];
            var auxiliaryResource = (SceneCompositorContextCaptureDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary)
                .Objects[0];
            capture.ThrowOnResourceDispose = true;

            InvalidOperationException? error = detach
                ? Assert.Throws<InvalidOperationException>(() => element.RemoveObject(capture))
                : Assert.Throws<InvalidOperationException>(compositor.Dispose);

            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("dispose-Frame"));
                Assert.That(frameResource.DisposeCount, Is.EqualTo(1));
                Assert.That(auxiliaryResource.DisposeCount, Is.EqualTo(1));
            });

            capture.ThrowOnResourceDispose = false;
            Assert.DoesNotThrow(compositor.Dispose,
                "cleanup must remove both cache entries before resource disposal begins");
            Assert.Multiple(() =>
            {
                Assert.That(frameResource.DisposeCount, Is.EqualTo(1));
                Assert.That(auxiliaryResource.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(BusyDescendantCleanupTrigger.Detach)]
    [TestCase(BusyDescendantCleanupTrigger.CallbackFailure)]
    [TestCase(BusyDescendantCleanupTrigger.CompositorDispose)]
    public void ResourceCleanup_BusyDescendantRetainsSlotOwnershipUntilRetry(
        BusyDescendantCleanupTrigger trigger)
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        Task? childUpdate = null;
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        var child = new SceneCompositorConcurrencyProbeEffect();
        try
        {
            Scene scene = CreateScene(basePath);
            var owner = new SceneCompositorConcurrencyProbeDrawable();
            owner.FilterEffect.CurrentValue = child;
            Element element = CreateElement(basePath, isEnabled: true, owner);
            scene.Children.Add(element);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            var ownerResource = compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame)
                .Objects.OfType<SceneCompositorConcurrencyProbeDrawable.Resource>().Single();
            var childResource = (SceneCompositorConcurrencyProbeEffect.Resource)ownerResource.FilterEffect!;

            child.ResourceCallback = (_, _) =>
            {
                callbackEntered.Set();
                if (!releaseCallback.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The busy descendant callback timed out.");
                }
            };
            childUpdate = Task.Run(() =>
            {
                bool updateOnly = false;
                childResource.Update(child, new CompositionContext(TimeSpan.Zero), ref updateOnly);
            });
            Assert.That(callbackEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);

            InvalidOperationException? firstFailure;
            switch (trigger)
            {
                case BusyDescendantCleanupTrigger.Detach:
                    firstFailure = Assert.Throws<InvalidOperationException>(() => scene.Children.Remove(element));
                    break;
                case BusyDescendantCleanupTrigger.CallbackFailure:
                    firstFailure = Assert.Throws<InvalidOperationException>(() => compositor.EvaluateGraphics(
                        TimeSpan.FromMilliseconds(100),
                        RenderPullPurpose.Frame));
                    break;
                case BusyDescendantCleanupTrigger.CompositorDispose:
                    firstFailure = Assert.Throws<InvalidOperationException>(compositor.Dispose);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(trigger));
            }

            Assert.Multiple(() =>
            {
                Assert.That(firstFailure, Is.Not.Null);
                Assert.That(ownerResource.IsDisposed, Is.False,
                    "a rejected graph reservation must leave the cached owner live");
                Assert.That(childResource.IsDisposed, Is.False,
                    "a rejected graph reservation must not dispose the busy descendant");
                Assert.That(ownerResource.FilterEffect, Is.SameAs(childResource),
                    "the owner must retain the exact child until cleanup commits");
            });

            child.ResourceCallback = null;
            releaseCallback.Set();
            Assert.That(CaptureTaskFailure(childUpdate), Is.Null);

            if (trigger == BusyDescendantCleanupTrigger.CallbackFailure)
            {
                var replacement = compositor.EvaluateGraphics(
                    TimeSpan.FromMilliseconds(200),
                    RenderPullPurpose.Frame).Objects
                    .OfType<SceneCompositorConcurrencyProbeDrawable.Resource>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(replacement, Is.Not.SameAs(ownerResource),
                        "the partially updated resource must be evicted after pending cleanup succeeds");
                    Assert.That(ownerResource.IsDisposed, Is.True);
                    Assert.That(childResource.IsDisposed, Is.True);
                    Assert.That(replacement.IsDisposed, Is.False);
                });
            }
            else
            {
                Assert.DoesNotThrow(compositor.Dispose);
                Assert.Multiple(() =>
                {
                    Assert.That(ownerResource.IsDisposed, Is.True);
                    Assert.That(childResource.IsDisposed, Is.True);
                });
            }
        }
        finally
        {
            child.ResourceCallback = null;
            releaseCallback.Set();
            if (childUpdate != null)
            {
                _ = CaptureTaskFailure(childUpdate);
            }

            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(false, false, TestName = "ToResource callback / detach")]
    [TestCase(false, true, TestName = "ToResource callback / compositor dispose")]
    [TestCase(true, false, TestName = "Resource.Update callback / detach")]
    [TestCase(true, true, TestName = "Resource.Update callback / compositor dispose")]
    public void ResourceCallback_DetachAndDisposeDoNotWaitForBusyOperation(
        bool updateExistingFrame,
        bool disposeCompositor)
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        using var callbackBarrier = new Barrier(2);
        using var releaseCallback = new ManualResetEventSlim();
        try
        {
            Scene scene = CreateScene(basePath);
            var probe = new SceneCompositorConcurrencyProbeDrawable();
            Element element = CreateElement(basePath, isEnabled: true, probe);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            var auxiliaryResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary)
                .Objects[0];
            SceneCompositorConcurrencyProbeDrawable.Resource? existingFrameResource = null;
            if (updateExistingFrame)
            {
                existingFrameResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                    .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame)
                    .Objects[0];
            }

            probe.ResourceCallback = (_, context) =>
            {
                if (context.PullPurpose != RenderPullPurpose.Frame)
                {
                    return;
                }

                if (!callbackBarrier.SignalAndWait(TimeSpan.FromSeconds(10))
                    || !releaseCallback.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The SceneCompositor concurrency test barrier timed out.");
                }
            };

            Task<CompositionFrame> evaluation = Task.Run(() => compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(100),
                RenderPullPurpose.Frame));
            Assert.That(
                callbackBarrier.SignalAndWait(TimeSpan.FromSeconds(10)),
                Is.True,
                "the resource callback must enter before invalidation starts");

            Task invalidation = Task.Run(() =>
            {
                if (disposeCompositor)
                {
                    compositor.Dispose();
                }
                else
                {
                    element.RemoveObject(probe);
                }
            });

            bool invalidationCompletedWhileCallbackWasBlocked = SpinWait.SpinUntil(
                () => invalidation.IsCompleted,
                TimeSpan.FromSeconds(2));
            var busyFrameResource = probe.LastCallbackResource;
            int auxiliaryDisposeCountBeforeRelease = auxiliaryResource.DisposeCount;
            int frameDisposeCountBeforeRelease = busyFrameResource?.DisposeCount ?? -1;

            releaseCallback.Set();

            Exception? invalidationFailure = CaptureTaskFailure(invalidation);
            Exception? evaluationFailure = CaptureTaskFailure(evaluation);
            probe.ResourceCallback = null;
            compositor.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(invalidationCompletedWhileCallbackWasBlocked, Is.True,
                    "detach/dispose must invalidate a busy generation without waiting for its callback");
                Assert.That(invalidationFailure, Is.Null);
                Assert.That(
                    evaluationFailure,
                    disposeCompositor
                        ? Is.TypeOf<ObjectDisposedException>()
                        : Is.TypeOf<InvalidOperationException>());
                Assert.That(busyFrameResource, Is.Not.Null);
                Assert.That(busyFrameResource, Is.Not.SameAs(auxiliaryResource));
                Assert.That(auxiliaryDisposeCountBeforeRelease, Is.EqualTo(1),
                    "the idle auxiliary cache must be swept before invalidation returns");
                Assert.That(frameDisposeCountBeforeRelease, Is.Zero,
                    "the busy frame resource remains owned by its callback until completion");
                Assert.That(auxiliaryResource.DisposeCount, Is.EqualTo(1));
                Assert.That(busyFrameResource!.DisposeCount, Is.EqualTo(1));
                if (existingFrameResource != null)
                {
                    Assert.That(busyFrameResource, Is.SameAs(existingFrameResource));
                }
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            // Best effort only: normal paths release before assertions. This prevents a failed setup assertion
            // from stranding the evaluation task in its resource callback.
            releaseCallback.Set();

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(false, TestName = "Generation invalidation precedes cleanup failure")]
    [TestCase(true, TestName = "Resource callback failure precedes generation invalidation and cleanup failure")]
    public void ResourceCallback_PreservesPrimaryFailureWhenInvalidatedCleanupFails(
        bool throwFromCallback)
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var probe = new SceneCompositorConcurrencyProbeDrawable();
            Element element = CreateElement(basePath, isEnabled: true, probe);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            var auxiliaryResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary)
                .Objects[0];
            var frameResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame)
                .Objects[0];
            var callbackFailure = new ApplicationException("resource-callback-failure");
            var cleanupFailure = new InvalidOperationException("resource-cleanup-failure");
            probe.ResourceCallback = (resource, context) =>
            {
                if (context.PullPurpose != RenderPullPurpose.Frame)
                {
                    return;
                }

                resource.DisposeFailure = cleanupFailure;
                element.RemoveObject(probe);
                if (throwFromCallback)
                {
                    throw callbackFailure;
                }
            };

            Exception? error = Assert.Catch(() => compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(100),
                RenderPullPurpose.Frame));
            probe.ResourceCallback = null;
            compositor.Dispose();

            Assert.Multiple(() =>
            {
                if (throwFromCallback)
                {
                    Assert.That(error, Is.SameAs(callbackFailure),
                        "the callback's exception identity is the primary operation failure");
                }
                else
                {
                    Assert.That(error, Is.TypeOf<InvalidOperationException>());
                    Assert.That(error, Is.Not.SameAs(cleanupFailure));
                    Assert.That(error!.Message, Does.Contain("detached"),
                        "generation invalidation must not be masked by Resource.Dispose");
                }

                Assert.That(frameResource, Is.Not.Null);
                Assert.That(frameResource, Is.Not.SameAs(auxiliaryResource));
                Assert.That(frameResource!.DisposeCount, Is.EqualTo(1));
                Assert.That(auxiliaryResource.DisposeCount, Is.EqualTo(1));
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void ResourceUpdateFailure_EvictsPartialResourceAndPreservesCallbackFailure()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var probe = new SceneCompositorConcurrencyProbeDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, probe));
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            var frameResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame)
                .Objects[0];
            var auxiliaryResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary)
                .Objects[0];
            var callbackFailure = new ApplicationException("resource-update-failure");
            var cleanupFailure = new InvalidOperationException("resource-cleanup-failure");
            probe.ResourceCallback = (resource, context) =>
            {
                if (context.PullPurpose == RenderPullPurpose.Frame)
                {
                    resource.DisposeFailure = cleanupFailure;
                    throw callbackFailure;
                }
            };

            Exception? error = Assert.Catch(() => compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(100),
                RenderPullPurpose.Frame));
            probe.ResourceCallback = null;

            var replacementFrameResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.FromMilliseconds(200), RenderPullPurpose.Frame)
                .Objects[0];
            var retainedAuxiliaryResource = (SceneCompositorConcurrencyProbeDrawable.Resource)compositor
                .EvaluateGraphics(TimeSpan.FromMilliseconds(200), RenderPullPurpose.Auxiliary)
                .Objects[0];
            compositor.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(error, Is.SameAs(callbackFailure),
                    "Resource.Dispose cleanup must not mask the Update callback failure");
                Assert.That(frameResource.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementFrameResource, Is.Not.SameAs(frameResource),
                    "an in-place Update failure must evict its potentially partial resource");
                Assert.That(replacementFrameResource.DisposeCount, Is.EqualTo(1));
                Assert.That(retainedAuxiliaryResource, Is.SameAs(auxiliaryResource),
                    "evicting the frame slot must not disturb the auxiliary cache");
                Assert.That(auxiliaryResource.DisposeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void ResourceCallback_CrossThreadSynchronousReentryFailsFast()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        Task<Exception?>? nestedEvaluation = null;
        using var nestedStarted = new Barrier(2);
        try
        {
            Scene scene = CreateScene(basePath);
            var probe = new SceneCompositorConcurrencyProbeDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, probe));
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            SceneCompositor activeCompositor = compositor;
            var outerTime = TimeSpan.FromMilliseconds(100);
            var nestedTime = TimeSpan.FromMilliseconds(200);

            var frameResource = (SceneCompositorConcurrencyProbeDrawable.Resource)activeCompositor
                .EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame)
                .Objects[0];
            Exception? nestedFailure = null;
            probe.ResourceCallback = (_, context) =>
            {
                if (context.Time != outerTime)
                {
                    return;
                }

                nestedEvaluation = Task.Run(() =>
                {
                    if (!nestedStarted.SignalAndWait(TimeSpan.FromSeconds(10)))
                    {
                        return new TimeoutException("The nested evaluation did not reach its barrier.");
                    }

                    try
                    {
                        activeCompositor.EvaluateGraphics(nestedTime, RenderPullPurpose.Frame);
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                if (!nestedStarted.SignalAndWait(TimeSpan.FromSeconds(10))
                    || !nestedEvaluation.Wait(TimeSpan.FromSeconds(2)))
                {
                    throw new TimeoutException(
                        "A cross-thread synchronous re-entry waited for its own busy resource operation.");
                }

                nestedFailure = nestedEvaluation.GetAwaiter().GetResult();
            };

            CompositionFrame outerFrame = activeCompositor.EvaluateGraphics(
                outerTime,
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(nestedFailure, Is.TypeOf<InvalidOperationException>());
                Assert.That(
                    nestedFailure!.Message,
                    Is.EqualTo(
                        "A composition resource callback cannot synchronously re-enter "
                        + "the same SceneCompositor evaluation."));
                Assert.That(outerFrame.Objects[0], Is.SameAs(frameResource),
                    "rejecting the nested operation must leave the outer slot publishable");
                Assert.That(frameResource.DisposeCount, Is.Zero);
            });
        }
        finally
        {
            if (nestedEvaluation != null)
            {
                Assert.That(nestedEvaluation.Wait(TimeSpan.FromSeconds(10)), Is.True);
            }

            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void CrossPurposeSynchronousReentry_RejectsBothSidesBeforeEvaluationGateDeadlock()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        Task<Exception?>? frameEvaluation = null;
        Task<Exception?>? auxiliaryEvaluation = null;
        using var outerCallbacksEntered = new Barrier(2);
        try
        {
            Scene scene = CreateScene(basePath);
            var probe = new SceneCompositorConcurrencyProbeDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, probe));
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            SceneCompositor activeCompositor = compositor;
            var frameTime = TimeSpan.FromMilliseconds(100);
            var auxiliaryTime = TimeSpan.FromMilliseconds(200);

            activeCompositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame);
            activeCompositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary);
            probe.ResourceCallback = (_, context) =>
            {
                RenderPullPurpose nestedPurpose;
                TimeSpan nestedTime;
                if (context.Time == frameTime)
                {
                    nestedPurpose = RenderPullPurpose.Auxiliary;
                    nestedTime = TimeSpan.FromMilliseconds(300);
                }
                else if (context.Time == auxiliaryTime)
                {
                    nestedPurpose = RenderPullPurpose.Frame;
                    nestedTime = TimeSpan.FromMilliseconds(400);
                }
                else
                {
                    return;
                }

                if (!outerCallbacksEntered.SignalAndWait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Both outer evaluations must hold their purpose-specific gates before re-entry.");
                }

                activeCompositor.EvaluateGraphics(nestedTime, nestedPurpose);
            };

            frameEvaluation = Task.Run(() =>
            {
                try
                {
                    activeCompositor.EvaluateGraphics(frameTime, RenderPullPurpose.Frame);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });
            auxiliaryEvaluation = Task.Run(() =>
            {
                try
                {
                    activeCompositor.EvaluateGraphics(auxiliaryTime, RenderPullPurpose.Auxiliary);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            Assert.That(
                Task.WaitAll([frameEvaluation, auxiliaryEvaluation], TimeSpan.FromSeconds(10)),
                Is.True,
                "cross-purpose re-entry must fail before either evaluation waits for the other gate");
            Assert.Multiple(() =>
            {
                Assert.That(frameEvaluation.Result, Is.TypeOf<InvalidOperationException>());
                Assert.That(auxiliaryEvaluation.Result, Is.TypeOf<InvalidOperationException>());
                Assert.That(frameEvaluation.Result!.Message, Does.Contain("same SceneCompositor"));
                Assert.That(auxiliaryEvaluation.Result!.Message, Does.Contain("same SceneCompositor"));
            });
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void SynchronousNestedEvaluation_OnDifferentCompositorRemainsAllowed()
    {
        string basePath = GetTempPath();
        try
        {
            Scene innerScene = CreateScene(basePath);
            var innerProbe = new SceneCompositorConcurrencyProbeDrawable();
            innerScene.Children.Add(CreateElement(basePath, isEnabled: true, innerProbe));
            using var innerCompositor = new SceneCompositor(innerScene, RenderIntent.Preview);

            Scene outerScene = CreateScene(basePath);
            var outerProbe = new SceneCompositorConcurrencyProbeDrawable();
            outerScene.Children.Add(CreateElement(basePath, isEnabled: true, outerProbe));
            using var outerCompositor = new SceneCompositor(outerScene, RenderIntent.Preview);
            var outerTime = TimeSpan.FromMilliseconds(100);
            CompositionFrame nestedFrame = default;

            outerCompositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame);
            outerProbe.ResourceCallback = (_, context) =>
            {
                if (context.Time == outerTime)
                {
                    nestedFrame = innerCompositor.EvaluateGraphics(
                        TimeSpan.FromMilliseconds(200),
                        RenderPullPurpose.Auxiliary);
                }
            };

            Assert.DoesNotThrow(() => outerCompositor.EvaluateGraphics(
                outerTime,
                RenderPullPurpose.Frame));
            Assert.That(nestedFrame.Objects.Length, Is.EqualTo(1));
            Assert.That(
                nestedFrame.Objects[0].GetOriginal(),
                Is.SameAs(innerProbe));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void SamePurposeEvaluations_AreSerializedAcrossTheWholeFrame()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        Task<CompositionFrame>? firstEvaluation = null;
        Task<CompositionFrame>? secondEvaluation = null;
        using var firstBlocked = new Barrier(2);
        using var releaseFirst = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        try
        {
            Scene scene = CreateScene(basePath);
            var first = new SceneCompositorConcurrencyProbeDrawable();
            var blocker = new SceneCompositorConcurrencyProbeDrawable();
            Element element = CreateElement(basePath, isEnabled: true, first);
            element.AddObject(blocker);
            scene.Children.Add(element);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            SceneCompositor activeCompositor = compositor;
            var firstTime = TimeSpan.FromMilliseconds(100);
            var secondTime = TimeSpan.FromMilliseconds(200);
            activeCompositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame);

            first.ResourceCallback = (_, context) =>
            {
                if (context.Time == secondTime)
                {
                    secondEntered.Set();
                }
            };
            blocker.ResourceCallback = (_, context) =>
            {
                if (context.Time == firstTime
                    && (!firstBlocked.SignalAndWait(TimeSpan.FromSeconds(10))
                        || !releaseFirst.Wait(TimeSpan.FromSeconds(10))))
                {
                    throw new TimeoutException("The first same-purpose evaluation barrier timed out.");
                }
            };

            firstEvaluation = Task.Run(() => activeCompositor.EvaluateGraphics(
                firstTime,
                RenderPullPurpose.Frame));
            Assert.That(firstBlocked.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
            secondEvaluation = Task.Run(() => activeCompositor.EvaluateGraphics(
                secondTime,
                RenderPullPurpose.Frame));

            Assert.Multiple(() =>
            {
                Assert.That(secondEntered.Wait(TimeSpan.FromMilliseconds(500)), Is.False,
                    "a newer frame must not mutate shared frame resources before the older frame is handed off");
                Assert.That(secondEvaluation.IsCompleted, Is.False);
            });

            releaseFirst.Set();
            Assert.DoesNotThrow(() => firstEvaluation.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => secondEvaluation.GetAwaiter().GetResult());
            Assert.That(secondEntered.IsSet, Is.True);
        }
        finally
        {
            releaseFirst.Set();
            if (firstEvaluation != null)
            {
                Assert.That(firstEvaluation.Wait(TimeSpan.FromSeconds(10)), Is.True);
            }

            if (secondEvaluation != null)
            {
                Assert.That(secondEvaluation.Wait(TimeSpan.FromSeconds(10)), Is.True);
            }

            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_ElementRemovedAfterLayerSnapshotIsNotResurrected()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        Task<CompositionFrame>? evaluation = null;
        using var callbackEntered = new Barrier(2);
        using var releaseCallback = new ManualResetEventSlim();
        try
        {
            Scene scene = CreateScene(basePath);
            var blocker = new SceneCompositorConcurrencyProbeDrawable();
            Element firstElement = CreateElement(basePath, isEnabled: true, blocker);
            firstElement.ZIndex = 0;
            var stale = new SceneCompositorConcurrencyProbeDrawable();
            Element staleElement = CreateElement(basePath, isEnabled: true, stale);
            staleElement.ZIndex = 1;
            scene.Children.Add(firstElement);
            scene.Children.Add(staleElement);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            SceneCompositor activeCompositor = compositor;
            var blockedTime = TimeSpan.FromMilliseconds(100);

            CompositionFrame initial = activeCompositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var staleResource = (SceneCompositorConcurrencyProbeDrawable.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), stale));
            blocker.ResourceCallback = (_, context) =>
            {
                if (context.Time == blockedTime
                    && (!callbackEntered.SignalAndWait(TimeSpan.FromSeconds(10))
                        || !releaseCallback.Wait(TimeSpan.FromSeconds(10))))
                {
                    throw new TimeoutException("The stale-element callback barrier timed out.");
                }
            };

            evaluation = Task.Run(() => activeCompositor.EvaluateGraphics(
                blockedTime,
                RenderPullPurpose.Frame));
            Assert.That(callbackEntered.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
            scene.Children.Remove(staleElement);
            releaseCallback.Set();

            CompositionFrame frame = evaluation.GetAwaiter().GetResult();
            Assert.Multiple(() =>
            {
                Assert.That(frame.Objects.Select(resource => resource.GetOriginal()),
                    Does.Not.Contain(stale));
                Assert.That(staleResource.DisposeCount, Is.EqualTo(1));
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            releaseCallback.Set();
            if (evaluation != null)
            {
                Assert.That(evaluation.Wait(TimeSpan.FromSeconds(10)), Is.True);
            }

            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_DetachedSiblingFromCollectedSnapshotIsNotResurrected()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        using var callbackEntered = new Barrier(2);
        using var releaseCallback = new ManualResetEventSlim();
        try
        {
            Scene scene = CreateScene(basePath);
            var blocker = new SceneCompositorConcurrencyProbeDrawable();
            var staleSibling = new SceneCompositorConcurrencyProbeDrawable();
            Element element = CreateElement(basePath, isEnabled: true, blocker);
            element.AddObject(staleSibling);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            SceneCompositor activeCompositor = compositor;
            var blockedTime = TimeSpan.FromMilliseconds(100);

            CompositionFrame initialFrame = activeCompositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var staleResource = (SceneCompositorConcurrencyProbeDrawable.Resource)initialFrame.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), staleSibling));
            int staleCallbacks = 0;
            blocker.ResourceCallback = (_, context) =>
            {
                if (context.Time == blockedTime
                    && (!callbackEntered.SignalAndWait(TimeSpan.FromSeconds(10))
                        || !releaseCallback.Wait(TimeSpan.FromSeconds(10))))
                {
                    throw new TimeoutException("The stale-sibling callback barrier timed out.");
                }
            };
            staleSibling.ResourceCallback = (_, _) => Interlocked.Increment(ref staleCallbacks);

            Task<CompositionFrame> evaluation = Task.Run(() => activeCompositor.EvaluateGraphics(
                blockedTime,
                RenderPullPurpose.Frame));
            Assert.That(callbackEntered.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);

            element.RemoveObject(staleSibling);
            releaseCallback.Set();
            CompositionFrame frameAfterDetach = evaluation.GetAwaiter().GetResult();

            Assert.Multiple(() =>
            {
                Assert.That(frameAfterDetach.Objects.Select(resource => resource.GetOriginal()),
                    Does.Not.Contain(staleSibling));
                Assert.That(staleCallbacks, Is.Zero,
                    "an object detached after CollectObjects must not start a new resource generation");
                Assert.That(staleResource.DisposeCount, Is.EqualTo(1));
            });

            element.AddObject(staleSibling);
            CompositionFrame frameAfterReattach = activeCompositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(200),
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(frameAfterReattach.Objects.Select(resource => resource.GetOriginal()),
                    Does.Contain(staleSibling));
                Assert.That(staleCallbacks, Is.EqualTo(1),
                    "a later snapshot after reattachment must start exactly one fresh generation");
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            releaseCallback.Set();
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_RecursivelyPublishedResourceDetachedLaterInFlowIsExcluded()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var portal = new PortalObject();
            portal.Count.CurrentValue = 1;
            var blocker = new SceneCompositorConcurrencyProbeDrawable();
            Element outer = CreateElement(basePath, isEnabled: true, portal);
            outer.ZIndex = 0;
            outer.AddObject(blocker);

            var nestedDrawable = new SceneCompositorConcurrencyProbeDrawable();
            Element nested = CreateElement(basePath, isEnabled: true, nestedDrawable);
            nested.ZIndex = 1;
            scene.Children.Add(outer);
            scene.Children.Add(nested);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            var invalidationTime = TimeSpan.FromMilliseconds(100);

            CompositionFrame initial = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var nestedResource = (SceneCompositorConcurrencyProbeDrawable.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), nestedDrawable));
            blocker.ResourceCallback = (_, context) =>
            {
                if (context.Time == invalidationTime)
                {
                    nested.RemoveObject(nestedDrawable);
                }
            };

            CompositionFrame invalidated = compositor.EvaluateGraphics(
                invalidationTime,
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(invalidated.Objects.Select(resource => resource.GetOriginal()),
                    Does.Not.Contain(nestedDrawable));
                Assert.That(nestedResource.DisposeCount, Is.EqualTo(1));
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_FlowConsumerWithDetachedDependencyIsExcluded()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var dependency = new SceneCompositorConcurrencyProbeDrawable();
            var group = new DrawableGroup();
            var blocker = new SceneCompositorConcurrencyProbeDrawable();
            Element element = CreateElement(basePath, isEnabled: true, dependency);
            element.AddObject(group);
            element.AddObject(blocker);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            var invalidationTime = TimeSpan.FromMilliseconds(100);

            CompositionFrame initial = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var groupResource = (DrawableGroup.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), group));
            var dependencyResource = (SceneCompositorConcurrencyProbeDrawable.Resource)
                groupResource.Children.Single();
            blocker.ResourceCallback = (_, context) =>
            {
                if (context.Time == invalidationTime)
                {
                    element.RemoveObject(dependency);
                }
            };

            CompositionFrame invalidated = compositor.EvaluateGraphics(
                invalidationTime,
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(invalidated.Objects.Select(resource => resource.GetOriginal()),
                    Does.Not.Contain(group),
                    "a flow consumer must not publish a disposed dependency through its cached children");
                Assert.That(dependencyResource.DisposeCount, Is.EqualTo(1));
                Assert.That(groupResource.Children.Single().IsDisposed, Is.True,
                    "the cached consumer may be repaired on a later update, but it must not escape this frame");
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_FlowReaderWithDetachedDependencyIsExcludedWithoutRemoval()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var dependency = new SceneCompositorConcurrencyProbeDrawable();
            var reader = new SceneCompositorFlowReaderDrawable();
            Element element = CreateElement(basePath, isEnabled: true, dependency);
            element.AddObject(reader);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            var invalidationTime = TimeSpan.FromMilliseconds(100);

            CompositionFrame initial = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var dependencyResource = (SceneCompositorConcurrencyProbeDrawable.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), dependency));
            var readerResource = (SceneCompositorFlowReaderDrawable.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), reader));
            reader.ResourceCallback = context =>
            {
                if (context.Time == invalidationTime)
                {
                    element.RemoveObject(dependency);
                }
            };

            CompositionFrame invalidated = compositor.EvaluateGraphics(
                invalidationTime,
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(invalidated.Objects.Select(resource => resource.GetOriginal()),
                    Does.Not.Contain(reader));
                Assert.That(dependencyResource.DisposeCount, Is.EqualTo(1));
                Assert.That(readerResource.Dependency, Is.SameAs(dependencyResource));
                Assert.That(readerResource.Dependency!.IsDisposed, Is.True,
                    "a reader that retains rather than removes a flow item must still inherit its publication token");
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_FlowUnreadSiblingRemainsWhenEarlierResourceIsDetached()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var dependency = new SceneCompositorConcurrencyProbeDrawable();
            var independent = new SceneCompositorConcurrencyProbeDrawable();
            var blocker = new SceneCompositorConcurrencyProbeDrawable();
            Element element = CreateElement(basePath, isEnabled: true, dependency);
            element.AddObject(independent);
            element.AddObject(blocker);
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            compositor = new SceneCompositor(scene, RenderIntent.Preview);
            var invalidationTime = TimeSpan.FromMilliseconds(100);

            CompositionFrame initial = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var dependencyResource = (SceneCompositorConcurrencyProbeDrawable.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), dependency));
            var independentResource = (SceneCompositorConcurrencyProbeDrawable.Resource)initial.Objects
                .Single(resource => ReferenceEquals(resource.GetOriginal(), independent));
            blocker.ResourceCallback = (_, context) =>
            {
                if (context.Time == invalidationTime)
                {
                    element.RemoveObject(dependency);
                }
            };

            CompositionFrame invalidated = compositor.EvaluateGraphics(
                invalidationTime,
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(invalidated.Objects.Select(resource => resource.GetOriginal()),
                    Does.Not.Contain(dependency));
                Assert.That(invalidated.Objects,
                    Does.Contain(independentResource),
                    "a later sibling that never reads Flow must not inherit the earlier resource's publication token");
                Assert.That(dependencyResource.DisposeCount, Is.EqualTo(1));
                Assert.That(independentResource.IsDisposed, Is.False);
            });

            GC.KeepAlive(hierarchyRoot);
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void EvaluateGraphics_FlowAddPublishesAddedResourceWithProducerProvenance()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var producer = new SceneCompositorFlowAddingDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, producer));
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            CompositionFrame frame = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);

            SceneCompositorFlowAddingDrawable.Resource producerResource = frame.Objects
                .OfType<SceneCompositorFlowAddingDrawable.Resource>()
                .Single();
            SceneCompositorConcurrencyProbeDrawable.Resource extraResource
                = producerResource.ExtraResource!;
            Assert.Multiple(() =>
            {
                Assert.That(frame.Objects.Length, Is.EqualTo(2));
                Assert.That(frame.Objects, Does.Contain(producerResource));
                Assert.That(frame.Objects, Does.Contain(extraResource),
                    "a resource added through the public Flow list must inherit its producer's provenance");
                Assert.That(extraResource.GetOriginal(), Is.SameAs(producer.Extra));
                Assert.That(extraResource.IsDisposed, Is.False);
            });
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void PurposeResourceCaches_RejectAliasedResourceWithoutDamagingExistingOwner()
    {
        string basePath = GetTempPath();
        SceneCompositor? compositor = null;
        try
        {
            Scene scene = CreateScene(basePath);
            var aliasing = new SceneCompositorAliasingDrawable();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, aliasing));
            compositor = new SceneCompositor(scene, RenderIntent.Preview);

            CompositionFrame frame = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            var frameResource = (SceneCompositorAliasingDrawable.Resource)frame.Objects[0];

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                compositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary));
            CompositionFrame nextFrame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(100),
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Does.Contain("distinct Resource instance"));
                Assert.That(nextFrame.Objects[0], Is.SameAs(frameResource),
                    "the contract rejection must leave the existing frame owner intact");
                Assert.That(frameResource.IsDisposed, Is.False);
                Assert.That(frameResource.DisposeCount, Is.Zero);
            });

            compositor.Dispose();
            Assert.That(frameResource.DisposeCount, Is.EqualTo(1));
        }
        finally
        {
            if (compositor != null)
            {
                Assert.DoesNotThrow(compositor.Dispose);
            }

            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(RenderPullPurpose.Frame)]
    [TestCase(RenderPullPurpose.Auxiliary)]
    public void SceneComposer_ExplicitDeliveryIntentSeedsAudioCompositionContext(
        RenderPullPurpose pullPurpose)
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureSound();
            scene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            using var composer = new SceneComposer(scene, RenderIntent.Delivery);

            CompositionFrame frame = composer.Compositor.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                pullPurpose);

            Assert.That(capture.ObservedRenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(capture.ObservedPullPurpose, Is.EqualTo(pullPurpose));
            Assert.That(frame.RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(frame.PullPurpose, Is.EqualTo(pullPurpose));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void SceneDrawable_PropagatesParentRenderPolicyIntoReferencedScene(RenderIntent renderIntent)
    {
        string basePath = GetTempPath();
        try
        {
            Scene childScene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            childScene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.CurrentValue = childScene;
            var context = new CompositionContext(
                TimeSpan.FromMilliseconds(500),
                renderIntent,
                RenderPullPurpose.Auxiliary);

            using Drawable.Resource resource = sceneDrawable.ToResource(context);

            Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
            Assert.That(capture.CapturedContexts[0].RenderIntent, Is.EqualTo(renderIntent));
            Assert.That(capture.CapturedContexts[0].PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void SceneSound_PropagatesParentRenderPolicyIntoReferencedScene(RenderIntent renderIntent)
    {
        string basePath = GetTempPath();
        try
        {
            Scene childScene = CreateScene(basePath);
            var capture = new SceneCompositorContextCaptureSound();
            childScene.Children.Add(CreateElement(basePath, isEnabled: true, capture));
            var sceneSound = new SceneSound();
            sceneSound.ReferencedScene.CurrentValue = childScene;
            var context = new CompositionContext(
                TimeSpan.Zero,
                renderIntent,
                RenderPullPurpose.Auxiliary);

            using var resource = (SceneSound.Resource)sceneSound.ToResource(context);
            resource._compositor!.EvaluateAudio(
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                resource.PullPurpose);

            Assert.That(capture.ObservedRenderIntent, Is.EqualTo(renderIntent));
            Assert.That(capture.ObservedPullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
        }
        finally
        {
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

    private sealed class LegacyFrameOnlyCompositor : ICompositor
    {
        public CompositionFrame EvaluateGraphics(TimeSpan time) => default;

        public CompositionFrame EvaluateAudio(TimeRange timeRange) => default;

        public void Dispose()
        {
        }
    }
}

internal sealed partial class SceneCompositorContextCaptureDrawable : Drawable
{
    public List<CapturedCompositionContext> CapturedContexts { get; } = [];

    public bool ThrowOnResourceDispose { get; set; }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        public int UpdateCount { get; private set; }

        public int DisposeCount { get; private set; }

        public TimeSpan LastTime { get; private set; }

        public RenderPullPurpose LastPullPurpose { get; private set; }

        partial void PostUpdate(SceneCompositorContextCaptureDrawable obj, CompositionContext context)
        {
            UpdateCount++;
            LastTime = context.Time;
            LastPullPurpose = context.PullPurpose;
            obj.CapturedContexts.Add(new CapturedCompositionContext(
                context.PreferProxy,
                context.RenderIntent,
                context.PullPurpose));
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            DisposeCount++;
            if (GetOriginal().ThrowOnResourceDispose)
            {
                throw new InvalidOperationException($"dispose-{LastPullPurpose}");
            }
        }
    }
}

internal readonly record struct CapturedCompositionContext(
    bool PreferProxy,
    RenderIntent RenderIntent,
    RenderPullPurpose PullPurpose);

internal sealed partial class SceneCompositorConcurrencyProbeEffect : FilterEffect
{
    public Action<Resource, CompositionContext>? ResourceCallback { get; set; }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public partial class Resource
    {
        partial void PostUpdate(
            SceneCompositorConcurrencyProbeEffect obj,
            CompositionContext context)
        {
            obj.ResourceCallback?.Invoke(this, context);
        }
    }
}

internal sealed partial class SceneCompositorConcurrencyProbeDrawable : Drawable
{
    private Resource? _lastCallbackResource;

    public Action<Resource, CompositionContext>? ResourceCallback { get; set; }

    public Resource? LastCallbackResource => Volatile.Read(ref _lastCallbackResource);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Exception? DisposeFailure { get; set; }

        partial void PostUpdate(
            SceneCompositorConcurrencyProbeDrawable obj,
            CompositionContext context)
        {
            Volatile.Write(ref obj._lastCallbackResource, this);
            obj.ResourceCallback?.Invoke(this, context);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
                if (DisposeFailure is { } failure)
                {
                    throw failure;
                }
            }
        }
    }
}

internal sealed partial class SceneCompositorFlowReaderDrawable : Drawable
{
    public Action<CompositionContext>? ResourceCallback { get; set; }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        public Drawable.Resource? Dependency { get; private set; }

        partial void PostUpdate(SceneCompositorFlowReaderDrawable obj, CompositionContext context)
        {
            Dependency = context.Flow?.OfType<Drawable.Resource>().FirstOrDefault();
            obj.ResourceCallback?.Invoke(context);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                Dependency = null;
            }
        }
    }
}

internal sealed partial class SceneCompositorFlowAddingDrawable : Drawable
{
    public SceneCompositorConcurrencyProbeDrawable Extra { get; } = new();

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        public SceneCompositorConcurrencyProbeDrawable.Resource? ExtraResource { get; private set; }

        partial void PostUpdate(SceneCompositorFlowAddingDrawable obj, CompositionContext context)
        {
            if (ExtraResource == null)
            {
                ExtraResource = (SceneCompositorConcurrencyProbeDrawable.Resource)obj.Extra.ToResource(context);
            }
            else
            {
                bool updateOnly = false;
                ExtraResource.Update(obj.Extra, context, ref updateOnly);
            }

            context.Flow?.Add(ExtraResource);
        }

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (disposing)
                context.Reserve(ExtraResource);
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            ExtraResource = null;
        }
    }
}

[Beutl.Engine.SuppressResourceClassGeneration]
internal sealed class SceneCompositorAliasingDrawable : Drawable
{
    private readonly Resource _resource = new();
    private int _initialized;

    public override Resource ToResource(CompositionContext context)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            bool updateOnly = true;
            _resource.Update(this, context, ref updateOnly);
        }

        return _resource;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public new sealed class Resource : Drawable.Resource
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed partial class SceneCompositorContextCaptureSound : Beutl.Audio.Sound
{
    public RenderIntent ObservedRenderIntent { get; private set; } = RenderIntent.Preview;

    public RenderPullPurpose ObservedPullPurpose { get; private set; } = RenderPullPurpose.Frame;

    public override void Compose(Beutl.Audio.Graph.AudioContext context, Beutl.Audio.Sound.Resource resource)
    {
    }

    public partial class Resource
    {
        public override Beutl.Media.Source.SoundSource.Resource? GetSoundSource() => null;

        partial void PostUpdate(SceneCompositorContextCaptureSound obj, CompositionContext context)
        {
            obj.ObservedRenderIntent = context.RenderIntent;
            obj.ObservedPullPurpose = context.PullPurpose;
        }
    }
}
