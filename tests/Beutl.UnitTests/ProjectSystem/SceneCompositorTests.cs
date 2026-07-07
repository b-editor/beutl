using Beutl.Composition;
using Beutl.Engine;
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
