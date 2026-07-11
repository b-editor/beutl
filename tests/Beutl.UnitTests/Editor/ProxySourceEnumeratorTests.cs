using System.Reflection;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Effects;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
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
    public void EnumerateVideoSources_RecursesIntoDrawableDecoratorChildren()
    {
        var nested = new SourceVideo();
        nested.Source.CurrentValue = CreateVideoSource("decorated.mov");
        var decorator = new DrawableDecorator();
        decorator.Children.Add(nested);
        Element element = ElementWith(decorator);

        Assert.That(FileNames(element), Does.Contain("decorated.mov"));
    }

    // A DrawableTextureSource on a 3D material (Object3D.Material.DiffuseMap) renders its nested Drawable
    // via GetTexture, opening that drawable's files; the media walk must reach a SourceVideo inside it.
    [Test]
    public void EnumerateVideoSources_RecursesIntoDrawableTextureSourceDrawable()
    {
        var video = new SourceVideo();
        video.Source.CurrentValue = CreateVideoSource("texture-drawable.mov");
        var textureSource = new DrawableTextureSource();
        textureSource.Drawable.CurrentValue = video;
        var material = new BasicMaterial();
        material.DiffuseMap.CurrentValue = textureSource;
        var cube = new Cube3D();
        cube.Material.CurrentValue = material;
        Element element = ElementWith(cube);

        Assert.That(FileNames(element), Does.Contain("texture-drawable.mov"));
    }

    [Test]
    public void EnumerateVideoSources_RecursesIntoDrawableTimeControllerTarget()
    {
        var nested = new SourceVideo();
        nested.Source.CurrentValue = CreateVideoSource("retimed.mov");
        var controller = new DrawableTimeController();
        controller.Target.CurrentValue = nested;
        Element element = ElementWith(controller);

        Assert.That(FileNames(element), Does.Contain("retimed.mov"));
    }

    [Test]
    public void EnumerateVideoSources_RecursesIntoDrawablePresenterTarget()
    {
        var nested = new SourceVideo();
        nested.Source.CurrentValue = CreateVideoSource("presented.mov");
        var presenter = new DrawablePresenter();
        presenter.Target.CurrentValue = nested;
        Element element = ElementWith(presenter);

        Assert.That(FileNames(element), Does.Contain("presented.mov"));
    }

    // The render path skips a disabled nested child (DrawableGroup.OnDraw -> DrawDrawable ->
    // Drawable.Render), so export preflight (skipDisabledElements) must not demand its file either.
    [Test]
    public void EnumerateFileSources_SkipDisabled_ExcludesDisabledDrawableGroupChild()
    {
        var enabled = new SourceVideo();
        enabled.Source.CurrentValue = CreateVideoSource("enabled-child.mov");
        var disabled = new SourceVideo { IsEnabled = false };
        disabled.Source.CurrentValue = CreateVideoSource("disabled-child.mov");
        var group = new DrawableGroup();
        group.Children.Add(enabled);
        group.Children.Add(disabled);
        Element element = ElementWith(group);

        IEnumerable<string> preflight = ProxySourceEnumerator
            .EnumerateFileSources(element, skipDisabledElements: true)
            .OfType<VideoSource>().Select(FileName);
        IEnumerable<string> full = ProxySourceEnumerator
            .EnumerateFileSources(element)
            .OfType<VideoSource>().Select(FileName);

        Assert.Multiple(() =>
        {
            Assert.That(preflight, Does.Contain("enabled-child.mov"));
            Assert.That(preflight, Does.Not.Contain("disabled-child.mov"));
            Assert.That(full, Does.Contain("disabled-child.mov"),
                "without the skip flag a disabled child still contributes a source");
        });
    }

    // A disabled presenter target is not rendered either, so the same skip applies before descending
    // into a DrawablePresenter/DrawableTimeController target.
    [Test]
    public void EnumerateFileSources_SkipDisabled_ExcludesDisabledPresenterTarget()
    {
        var disabled = new SourceVideo { IsEnabled = false };
        disabled.Source.CurrentValue = CreateVideoSource("disabled-target.mov");
        var presenter = new DrawablePresenter();
        presenter.Target.CurrentValue = disabled;
        Element element = ElementWith(presenter);

        Assert.Multiple(() =>
        {
            Assert.That(
                ProxySourceEnumerator.EnumerateFileSources(element, skipDisabledElements: true)
                    .OfType<VideoSource>().Select(FileName),
                Does.Not.Contain("disabled-target.mov"));
            Assert.That(
                ProxySourceEnumerator.EnumerateFileSources(element)
                    .OfType<VideoSource>().Select(FileName),
                Does.Contain("disabled-target.mov"));
        });
    }

    // FilterEffectRenderNode returns its input for a disabled effect, so a media source inside a
    // disabled filter effect never renders; export preflight must not demand its file either.
    [Test]
    public void EnumerateFileSources_SkipDisabled_ExcludesSourceInsideDisabledFilterEffect()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("disabled-fx.mov"));
        var graphEffect = new NodeGraphFilterEffect { IsEnabled = false };
        graphEffect.Model.CurrentValue!.Nodes.Add(node);
        var drawable = new SourceVideo();
        ((FilterEffectGroup)drawable.FilterEffect.CurrentValue!).Children.Add(graphEffect);
        Element element = ElementWith(drawable);

        Assert.Multiple(() =>
        {
            Assert.That(
                ProxySourceEnumerator.EnumerateFileSources(element, skipDisabledElements: true)
                    .OfType<VideoSource>().Select(FileName),
                Does.Not.Contain("disabled-fx.mov"));
            Assert.That(
                ProxySourceEnumerator.EnumerateFileSources(element)
                    .OfType<VideoSource>().Select(FileName),
                Does.Contain("disabled-fx.mov"),
                "without the skip flag a disabled filter-effect source still contributes");
        });
    }

    // Target is a reference property, so a user can point a presenter at its own ancestor;
    // the visited-target set must terminate the walk instead of recursing forever. The cycle is
    // wired silently (reflection) because completing it through the property setter trips a
    // pre-existing engine stack overflow in the Edited-forwarding chain — the state is otherwise
    // reachable via deserialization of a saved cyclic project.
    [Test]
    public void EnumerateVideoSources_TerminatesOnPresenterTargetCycle()
    {
        var video = new SourceVideo();
        video.Source.CurrentValue = CreateVideoSource("cycled.mov");
        var group = new DrawableGroup();
        group.Children.Add(video);
        var presenter = new DrawablePresenter();
        group.Children.Add(presenter);
        SetPropertyValueSilently(presenter.Target, group);
        Element element = ElementWith(group);

        Assert.That(FileNames(element), Does.Contain("cycled.mov"));
    }

    // SimpleProperty attaches a referenced scene as a hierarchical child, so mutually-referencing
    // scenes form a hierarchy cycle; the broad walk must terminate instead of overflowing the
    // stack. The cycle is wired via AddChild because completing it through the property setter
    // trips the same pre-existing Edited-forwarding stack overflow noted above.
    [Test]
    public void EnumerateFileSources_TerminatesOnMutuallyReferencingScenes()
    {
        Scene sceneA = CreateScene("mutual-a.scene");
        Scene sceneB = CreateScene("mutual-b.scene");

        var videoA = new SourceVideo();
        videoA.Source.CurrentValue = CreateVideoSource("in-a.mov");
        var referenceToB = new SceneDrawable();
        sceneA.Children.Add(ElementWith(videoA, referenceToB));

        var videoB = new SourceVideo();
        videoB.Source.CurrentValue = CreateVideoSource("in-b.mov");
        var referenceToA = new SceneDrawable();
        sceneB.Children.Add(ElementWith(videoB, referenceToA));

        ((IModifiableHierarchical)referenceToB).AddChild(sceneB);
        ((IModifiableHierarchical)referenceToA).AddChild(sceneA);

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(sceneA);

        Assert.That(collected.Select(Path.GetFileName), Is.SupersetOf(new[] { "in-a.mov", "in-b.mov" }));
    }

    private static void SetPropertyValueSilently<T>(IProperty<T> property, T value)
    {
        property.GetType()
            .GetField("_currentValue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(property, value);
    }

    [Test]
    public void EnumerateVideoSources_RecursesIntoFilterEffectPresenterTarget()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("presented-fx.mov"));
        var graphEffect = new NodeGraphFilterEffect();
        graphEffect.Model.CurrentValue!.Nodes.Add(node);
        var presenter = new FilterEffectPresenter();
        presenter.Target.CurrentValue = graphEffect;

        var drawable = new SourceVideo();
        ((FilterEffectGroup)drawable.FilterEffect.CurrentValue!).Children.Add(presenter);
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Does.Contain("presented-fx.mov"));
    }

    [Test]
    public void EnumerateVideoSources_RecursesIntoDelayAnimationEffectChild()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("delayed-fx.mov"));
        var graphEffect = new NodeGraphFilterEffect();
        graphEffect.Model.CurrentValue!.Nodes.Add(node);
        var delay = new DelayAnimationEffect();
        ((FilterEffectGroup)delay.Effect.CurrentValue!).Children.Add(graphEffect);

        var drawable = new SourceVideo();
        ((FilterEffectGroup)drawable.FilterEffect.CurrentValue!).Children.Add(delay);
        Element element = ElementWith(drawable);

        Assert.That(FileNames(element), Does.Contain("delayed-fx.mov"));
    }

    // A windowed FilterEffectPresenter and a full-walk DelayAnimationEffect can target the SAME filter
    // effect. The presenter is visited first with a render window that overrides the node input's base
    // value; if that windowed visit deduped the target in the shared identity set, the delay's remapped
    // full walk — which must surface the overridden base — would be wrongly suppressed. The separate
    // full-walk visited set keeps the full walk alive after the earlier windowed visit.
    [Test]
    public void EnumerateFileSources_WindowedPresenterDoesNotSuppressDelayFullWalkOfSharedTarget()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("full-walk-base.mov"));
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(0.5),
            Value = CreateVideoSource("windowed-anim.mov"),
        });
        ((IAnimatablePropertyAdapter<VideoSource?>)node.Source.Property!).Animation = animation;

        var graphEffect = new NodeGraphFilterEffect();
        graphEffect.Model.CurrentValue!.Nodes.Add(node);

        // The presenter (windowed, threads localRange) is visited before the delay (remapped full walk),
        // both pointing at the same graphEffect.
        var presenter = new FilterEffectPresenter();
        presenter.Target.CurrentValue = graphEffect;
        var delay = new DelayAnimationEffect();
        ((FilterEffectGroup)delay.Effect.CurrentValue!).Children.Add(graphEffect);

        var drawable = new SourceVideo();
        var chain = (FilterEffectGroup)drawable.FilterEffect.CurrentValue!;
        chain.Children.Add(presenter);
        chain.Children.Add(delay);
        Element element = ElementWith(drawable);

        var window = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        IEnumerable<string> names = ProxySourceEnumerator
            .EnumerateFileSources(element, localRange: window)
            .OfType<VideoSource>()
            .Select(FileName);

        // The windowed presenter visit drops the overridden base; only the delay's full walk surfaces it.
        Assert.That(names, Does.Contain("full-walk-base.mov"));
    }

    // Same windowed-then-full-walk hazard as above, but the shared target is a FilterEffectGroup. The
    // group traversal must not be gated by an identity-only visited set: a windowed presenter visit of
    // the group would otherwise suppress the delay's full walk of the same group's children.
    [Test]
    public void EnumerateFileSources_WindowedPresenterDoesNotSuppressDelayFullWalkOfSharedGroup()
    {
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(CreateVideoSource("group-full-walk-base.mov"));
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(0.5),
            Value = CreateVideoSource("group-windowed-anim.mov"),
        });
        ((IAnimatablePropertyAdapter<VideoSource?>)node.Source.Property!).Animation = animation;

        var graphEffect = new NodeGraphFilterEffect();
        graphEffect.Model.CurrentValue!.Nodes.Add(node);
        var shared = new FilterEffectGroup();
        shared.Children.Add(graphEffect);

        var presenter = new FilterEffectPresenter();
        presenter.Target.CurrentValue = shared;
        var delay = new DelayAnimationEffect();
        delay.Effect.CurrentValue = shared;

        var drawable = new SourceVideo();
        var chain = (FilterEffectGroup)drawable.FilterEffect.CurrentValue!;
        chain.Children.Add(presenter);
        chain.Children.Add(delay);
        Element element = ElementWith(drawable);

        var window = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        IEnumerable<string> names = ProxySourceEnumerator
            .EnumerateFileSources(element, localRange: window)
            .OfType<VideoSource>()
            .Select(FileName);

        Assert.That(names, Does.Contain("group-full-walk-base.mov"));
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

    // AudioVisualizerDrawable.Source can be a SoundGroup; the graphics render composes the group and
    // its child Sounds, so a nested audio file must be enumerated. The value walk previously excluded
    // SoundGroup and stopped before the dedicated group walk (the enumerated object is the visualizer).
    [Test]
    public void EnumerateMediaFileSources_CoversSoundGroupInsideAudioVisualizer()
    {
        var nestedSound = new SourceSound();
        nestedSound.Source.CurrentValue = CreateSoundSource("visualized.mp3");
        var group = new SoundGroup();
        group.Children.Add(nestedSound);
        var visualizer = new AudioSpectrogramDrawable();
        visualizer.Source.CurrentValue = group;
        Scene root = CreateScene("visualizer.scene");
        root.Children.Add(ElementWith(visualizer));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(root);

        Assert.That(collected.Select(Path.GetFileName), Does.Contain("visualized.mp3"));
    }

    // A node input whose current value is an EngineObject holding a file source (GeometryShapeNode.Fill
    // set to an ImageBrush) is opened by ToResource at render, so preflight must recurse the value, not
    // only report values that are themselves IFileSource.
    [Test]
    public void EnumerateMediaFileSources_CoversObjectValuedNodeInput()
    {
        var brush = new ImageBrush();
        brush.Source.CurrentValue = CreateImageSource("node-fill.png");
        var node = new GeometryShapeNode();
        node.Fill.Property!.SetValue(brush);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        Scene root = CreateScene("node-fill.scene");
        root.Children.Add(ElementWith(drawable));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(root);

        Assert.That(collected.Select(Path.GetFileName), Does.Contain("node-fill.png"));
    }

    // A reference-expression makes IProperty.GetValue return another object's value ahead of the
    // base/animation, so the render opens whatever file source it resolves to. Preflight must resolve
    // the reference (by id, no evaluation) and enumerate the target's sources.
    [Test]
    public void EnumerateMediaFileSources_CoversReferenceExpressionTarget()
    {
        var referenced = new SourceImage();
        referenced.Source.CurrentValue = CreateImageSource("expr-target.png");

        var driven = new SourceImage();
        driven.Source.Expression = Expression.CreateReference<ImageSource>(referenced.Id, "Source");

        Scene root = CreateScene("expr.scene");
        root.Children.Add(ElementWith(referenced));
        root.Children.Add(ElementWith(driven));

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateMediaFileSources(root);

        Assert.That(collected.Select(Path.GetFileName), Does.Contain("expr-target.png"));
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
