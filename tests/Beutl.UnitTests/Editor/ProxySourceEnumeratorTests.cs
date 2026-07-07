using System.Reflection;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
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

    [Test]
    public void EnumerateVideoSources_RecursesIntoDrawableGroupChildren()
    {
        var nested = new SourceVideo();
        nested.Source.CurrentValue = CreateVideoSource("grouped.mov");
        var group = new DrawableGroup();
        group.Children.Add(nested);
        Element element = ElementWith(group);

        Assert.That(FileNames(element), Does.Contain("grouped.mov"));
    }

    [Test]
    public void EnumerateVideoSources_RecursesIntoGroupNodeSubgraph()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("group-node.mov"));
        var groupNode = new GroupNode();
        groupNode.Group.Nodes.Add(node);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(groupNode);
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Does.Contain("group-node.mov"));
    }

    [Test]
    public void EnumerateVideoSources_IncludesEverySharedGroupNodeOuterInput()
    {
        var first = new GroupNode();
        GraphGroup sharedGroup = first.Group;
        var innerNode = new VideoSourceNode();
        var groupInput = new GroupInput();
        sharedGroup.Nodes.Add(innerNode);
        sharedGroup.Nodes.Add(groupInput);
        Assert.That(groupInput.AddNodePort(innerNode.Source, out _), Is.True);
        PopulateGroupInputPorts(first);

        var second = new GroupNode();
        SetGroup(second, sharedGroup);
        PopulateGroupInputPorts(second);

        SetGroupNodeVideoInput(first, "first-outer.mov");
        SetGroupNodeVideoInput(second, "second-outer.mov");

        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(first);
        drawable.Model.CurrentValue!.Nodes.Add(second);
        Element element = ElementWith(drawable);

        Assert.That(
            FileNames(element),
            Is.EquivalentTo(new[] { "first-outer.mov", "second-outer.mov" }));
    }

    // The video walk must cover all three proxy-aware holders in a single element: a top-level
    // SourceVideo, a VideoSourceNode inside a NodeGraphDrawable, and a SourceVideo inside a
    // referenced scene (with the cycle guard letting the referenced scene's own sources resolve).
    [Test]
    public void EnumerateVideoSources_CoversSourceVideo_AndNodeGraph_AndReferencedScene()
    {
        var video = new SourceVideo();
        video.Source.CurrentValue = CreateVideoSource("direct.mov");

        var graphNode = new VideoSourceNode();
        graphNode.Source.Property!.SetValue(CreateVideoSource("graph.mov"));
        var graphDrawable = new NodeGraphDrawable();
        graphDrawable.Model.CurrentValue!.Nodes.Add(graphNode);

        Scene referenced = CreateScene("referenced.scene");
        var nestedVideo = new SourceVideo();
        nestedVideo.Source.CurrentValue = CreateVideoSource("nested.mov");
        referenced.Children.Add(ElementWith(nestedVideo));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = referenced;

        Element element = ElementWith(video, graphDrawable, sceneDrawable);

        Assert.That(
            FileNames(element),
            Is.EquivalentTo(new[] { "direct.mov", "graph.mov", "nested.mov" }));
    }

    // The single file-source walk subsumes the old Engine broad IFileSource walk + the UI video-only
    // union: a Scene with an Element holding SourceVideo (video), SourceSound (audio), SourceImage
    // (image), and a NodeGraph-held VideoSource must yield all four deduped paths. Audio and image
    // surface as plain IProperty<IFileSource?> on EngineObjects (broad walk); the graph-held video
    // only resolves through EnumerateVideoSources (video walk).
    [Test]
    public void EnumerateFileSources_CoversAllIFileSourceProperties_AndNodeGraphVideo()
    {
        var video = new SourceVideo();
        video.Source.CurrentValue = CreateVideoSource("video.mov");

        var sound = new SourceSound();
        sound.Source.CurrentValue = CreateSoundSource("audio.mp3");

        var image = new SourceImage();
        image.Source.CurrentValue = CreateImageSource("image.png");

        var graphNode = new VideoSourceNode();
        graphNode.Source.Property!.SetValue(CreateVideoSource("graph.mov"));
        var graphDrawable = new NodeGraphDrawable();
        graphDrawable.Model.CurrentValue!.Nodes.Add(graphNode);

        // Scene.Children_CollectionChanged dereferences Scene.Uri and Element.Uri, so both must be set.
        Scene scene = CreateScene("covers.scene");
        scene.Children.Add(ElementWith(video, sound, image, graphDrawable));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateFileSources(scene);

        Assert.That(
            collected.Select(Path.GetFileName),
            Is.SupersetOf(new[] { "video.mov", "audio.mp3", "image.png", "graph.mov" }));
    }

    // Export preflight must reach image/audio held inside a referenced scene: those are not
    // hierarchical children (the scene is a property value) and are not VideoSources, so neither
    // the broad property walk nor the old video-only deep walk surfaced them.
    [Test]
    public void EnumerateMediaFileSources_CoversImageAndSoundInsideReferencedScene()
    {
        Scene childScene = CreateScene("child.scene");
        var childImage = new SourceImage();
        childImage.Source.CurrentValue = CreateImageSource("nested-image.png");
        var childSound = new SourceSound();
        childSound.Source.CurrentValue = CreateSoundSource("nested-audio.mp3");
        childScene.Children.Add(ElementWith(childImage, childSound));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        Scene root = CreateScene("root.scene");
        root.Children.Add(ElementWith(sceneDrawable));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(root);

        Assert.That(
            collected.Select(Path.GetFileName),
            Is.SupersetOf(new[] { "nested-image.png", "nested-audio.mp3" }));
    }

    // A SceneSound (a Sound, not a Drawable) referencing a scene must also have its nested media
    // enumerated; the deep walk descends Sound references, not just SceneDrawable.
    [Test]
    public void EnumerateMediaFileSources_CoversMediaInsideSceneSoundReference()
    {
        Scene childScene = CreateScene("sound-child.scene");
        var childSound = new SourceSound();
        childSound.Source.CurrentValue = CreateSoundSource("scene-sound.mp3");
        childScene.Children.Add(ElementWith(childSound));

        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.CurrentValue = childScene;
        Scene root = CreateScene("sound-root.scene");
        root.Children.Add(ElementWith(sceneSound));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(root);

        Assert.That(collected.Select(Path.GetFileName), Does.Contain("scene-sound.mp3"));
    }

    // A non-video graph input (ImageSourceNode.Source) is an IPropertyAdapter port, invisible to the
    // broad property walk; the export preflight must still enumerate it.
    [Test]
    public void EnumerateMediaFileSources_CoversImageSourceNodeGraphInput()
    {
        var node = new ImageSourceNode();
        node.Source.Property!.SetValue(CreateImageSource("graph-image.png"));
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        Scene root = CreateScene("graph-image.scene");
        root.Children.Add(ElementWith(drawable));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(root);

        Assert.That(collected.Select(Path.GetFileName), Does.Contain("graph-image.png"));
    }

    // EnumerateVideoSources stays video-only even though the shared walk now yields every IFileSource:
    // an ImageSourceNode input must not leak into the proxy-eligible video set.
    [Test]
    public void EnumerateVideoSources_ExcludesNonVideoGraphInputs()
    {
        var imageNode = new ImageSourceNode();
        imageNode.Source.Property!.SetValue(CreateImageSource("image.png"));
        var videoNode = new VideoSourceNode();
        videoNode.Source.Property!.SetValue(CreateVideoSource("video.mov"));
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(imageNode);
        drawable.Model.CurrentValue!.Nodes.Add(videoNode);
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Is.EquivalentTo(new[] { "video.mov" }));
    }

    // An IFileSource property with a keyframe animation referencing a different file must include
    // the animated file, so a proxy for media referenced only from a keyframe is protected.
    [Test]
    public void EnumerateFileSources_IncludesAnimatedKeyframeSources()
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
        Scene scene = CreateScene("animated.scene");
        scene.Children.Add(ElementWith(drawable));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateFileSources(scene);

        Assert.That(
            collected.Select(Path.GetFileName),
            Is.SupersetOf(new[] { "current.mov", "animated.mov" }));
    }

    private static IEnumerable<string> FileNames(Element element)
        => ProxySourceEnumerator.EnumerateVideoSources(element).Select(FileName);

    private static string FileName(VideoSource source) => Path.GetFileName(source.Uri.LocalPath);

    private static void SetGroup(GroupNode node, GraphGroup group)
    {
        typeof(GroupNode)
            .GetField("<Group>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, group);
    }

    private static void PopulateGroupInputPorts(GroupNode node)
    {
        typeof(GroupNode)
            .GetMethod("OnInputChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(node, [node.Group.Input, null]);
    }

    private static void SetGroupNodeVideoInput(GroupNode node, string fileName)
    {
        IInputPort input = node.Items
            .OfType<IInputPort>()
            .Single(item => item.Property?.PropertyType == typeof(VideoSource));
        input.Property!.SetValue(CreateVideoSource(fileName));
    }

    private static VideoSource CreateVideoSource(string fileName)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName)));
        return source;
    }

    private static SoundSource CreateSoundSource(string fileName)
    {
        var source = new SoundSource();
        source.ReadFrom(new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName)));
        return source;
    }

    private static ImageSource CreateImageSource(string fileName)
    {
        var source = new ImageSource();
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
