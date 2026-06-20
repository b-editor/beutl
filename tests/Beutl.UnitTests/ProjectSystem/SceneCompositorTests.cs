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
