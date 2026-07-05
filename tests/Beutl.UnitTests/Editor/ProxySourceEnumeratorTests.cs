using Beutl.Animation;
using Beutl.Editor.Components.ProxiesTab;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class ProxySourceEnumeratorTests
{
    [Test]
    public void EnumerateVideoSources_IncludesSourceVideoCurrentAndAnimatedValues()
    {
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = CreateVideoSource("current.mov");
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = CreateVideoSource("animated.mov"),
        });
        drawable.Source.Animation = animation;
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Is.EquivalentTo(new[] { "current.mov", "animated.mov" }));
    }

    [Test]
    public void EnumerateVideoSources_IncludesVideoSourceNodeInputs()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("node.mov"));
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Is.EquivalentTo(new[] { "node.mov" }));
    }

    [Test]
    public void EnumerateVideoSources_IncludesNodeGraphFilterEffectInputs()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("fx-node.mov"));
        var graphEffect = new NodeGraphFilterEffect();
        graphEffect.Model.CurrentValue!.Nodes.Add(node);

        var drawable = new SourceVideo();
        ((FilterEffectGroup)drawable.FilterEffect.CurrentValue!).Children.Add(graphEffect);
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Does.Contain("fx-node.mov"));
    }

    [Test]
    public void EnumerateVideoSources_RecursesIntoReferencedScenes()
    {
        Scene childScene = CreateScene("child.scene");
        var childVideo = new SourceVideo();
        childVideo.Source.CurrentValue = CreateVideoSource("nested.mov");
        childScene.Children.Add(ElementWith(childVideo));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        Element element = ElementWith(sceneDrawable);

        Assert.That(FileNames(element), Is.EquivalentTo(new[] { "nested.mov" }));
    }

    private static IEnumerable<string> FileNames(Element element)
        => ProxySourceEnumerator.EnumerateVideoSources(element).Select(FileName);

    private static string FileName(VideoSource source) => Path.GetFileName(source.Uri.LocalPath);

    private static VideoSource CreateVideoSource(string fileName)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName)));
        return source;
    }

    private static Element ElementWith(params EngineObject[] objects)
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.layer")),
        };
        foreach (EngineObject obj in objects)
            element.AddObject(obj);

        return element;
    }

    private static Scene CreateScene(string name)
    {
        return new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, name)),
        };
    }
}
