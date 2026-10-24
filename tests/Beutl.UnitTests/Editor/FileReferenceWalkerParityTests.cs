using Beutl.Animation;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Transitions;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor;

// Every walker that lists a document's files must find a file wherever the document can hold one. ProxySourceEnumerator
// is left out on purpose: it lists what the renderer opens, so it skips what the render path does not draw.
[TestFixture]
public class FileReferenceWalkerParityTests
{
    public enum Place { Property, KeyFrameValue, NodePort, NodePortKeyFrame, Transition, ReferencedScene, SerializedMember }

    public enum Walker { ExternalResourceCollector, MissingMediaFileUris, SerializedGraphTraversal, SerializedGraphPaths }

    private string _projectDir = null!;
    private string _mediaDir = null!;

    [SetUp]
    public void Setup()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), $"beutl_walkers_{Guid.NewGuid():N}");
        _mediaDir = Path.Combine(Path.GetTempPath(), $"beutl_walkers_media_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_mediaDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_projectDir, true);
        Directory.Delete(_mediaDir, true);
    }

    [Test]
    public void EveryWalkerFindsTheFileInEachPlace([Values] Walker walker, [Values] Place place)
    {
        Scene scene = CreateScene("main.scene");
        Uri expected = AddFileIn(scene, place);

        if (place == Place.ReferencedScene)
        {
            Assert.That(Find(walker, scene), Is.Empty);
            scene = scene.FindRequiredHierarchicalParent<Project>().Items.OfType<Scene>().Single(s => !ReferenceEquals(s, scene));
        }

        Assert.That(Find(walker, scene).Distinct(), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void EveryWalkerSkipsValuesThatAreNotSaved([Values] Walker walker)
    {
        Scene scene = CreateScene("main.scene");
        var holder = new SavedMemberHolder();
        holder.Unsaved.Value = Open<VideoSource>(new Uri(Path.Combine(_mediaDir, "unsaved.mov")));
        scene.Children.Add(ElementWith(holder));

        Assert.That(Find(walker, scene), Is.Empty);
    }

    [TestCase(Place.NodePort)]
    [TestCase(Place.NodePortKeyFrame)]
    [TestCase(Place.SerializedMember)]
    public async Task CollectedMembersCanBeRelocated(Place place)
    {
        Scene scene = CreateScene("main.scene");
        Uri original = AddFileIn(scene, place);
        File.WriteAllText(original.LocalPath, "media bytes");
        var project = new Project();
        project.Items.Add(scene);
        var collected = ExternalResourceCollector.Collect(project, _projectDir);

        RelocationResult result = await new ResourceRelocationService()
            .RelocateFileSourcesAsync(collected.FileSources, project, _projectDir);
        Uri relocated = MissingMediaService.GetFileUris(scene).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(relocated, Is.Not.EqualTo(original));
            Assert.That(File.ReadAllText(relocated.LocalPath), Is.EqualTo("media bytes"));
        });
    }

    private IEnumerable<Uri> Find(Walker walker, Scene scene) => walker switch
    {
        // Lists only files outside the project folder, where the media of this test are.
        Walker.ExternalResourceCollector => ExternalResourceCollector.Collect(scene, _projectDir).FileSources
            .Select(source => source.OriginalUri),
        Walker.MissingMediaFileUris => MissingMediaService.GetFileUris(scene),
        Walker.SerializedGraphPaths => scene.Children.SelectMany(SerializedGraphPaths.EnumerateSerializedGraphDescendantPaths)
            .Select(item => item.Object).OfType<IFileSource>().Select(source => source.Uri),
        _ => SerializedGraphTraversal.Enumerate(scene).OfType<IFileSource>().Select(source => source.Uri),
    };

    private Uri AddFileIn(Scene scene, Place place)
    {
        var uri = new Uri(Path.Combine(_mediaDir, place is Place.Property or Place.Transition ? "image.png" : "clip.mov"));
        Element element;
        switch (place)
        {
            case Place.Property:
                var image = new SourceImage();
                image.Source.Value = Open<ImageSource>(uri);
                element = ElementWith(image);
                break;
            case Place.KeyFrameValue:
                var video = new SourceVideo();
                var animation = new KeyFrameAnimation<VideoSource?>();
                animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = Open<VideoSource>(uri) } });
                video.Source.SetKeyFrameAnimation(animation);
                element = ElementWith(video);
                break;
            case Place.NodePort:
                var node = new VideoSourceNode();
                node.Source.Property!.SetValue(Open<VideoSource>(uri));
                var graph = new NodeGraphDrawable();
                graph.Model.Value!.Nodes.Add(node);
                element = ElementWith(graph);
                break;
            case Place.NodePortKeyFrame:
                var animatedNode = new VideoSourceNode();
                var portAnimation = new KeyFrameAnimation<VideoSource?>();
                portAnimation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = Open<VideoSource>(uri) } });
                ((IAnimatablePropertyAdapter)animatedNode.Source.Property!).Animation = portAnimation;
                var animatedGraph = new NodeGraphDrawable();
                animatedGraph.Model.Value!.Nodes.Add(animatedNode);
                element = ElementWith(animatedGraph);
                break;
            case Place.Transition:
                var transition = new ImageMaskTransition();
                transition.Mask.Value = Open<ImageSource>(uri);
                element = ElementWith(new SourceImage());
                element.EnterTransition.Value = transition;
                break;
            case Place.SerializedMember:
                element = ElementWith(new SavedMemberHolder
                {
                    Saved = new Dictionary<string, object> { ["wrapped"] = new SavedWrapper(Open<VideoSource>(uri)) },
                });
                break;
            default:
                Scene referenced = CreateScene("referenced.scene");
                var nested = new SourceVideo();
                nested.Source.Value = Open<VideoSource>(uri);
                referenced.Children.Add(ElementWith(nested));
                var drawable = new SceneDrawable();
                var project = new Project();
                project.Items.Add(scene);
                project.Items.Add(referenced);
                drawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(referenced.Id.Value);
                element = ElementWith(drawable);
                break;
        }

        scene.Children.Add(element);
        return uri;
    }

    private static T Open<T>(Uri uri) where T : IFileSource, new()
    {
        var source = new T();
        source.ReadFrom(uri);
        return source;
    }

    private Element ElementWith(EngineObject obj)
    {
        var element = new Element
        {
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(_projectDir, $"{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(obj);
        return element;
    }

    private Scene CreateScene(string name)
        => new(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_projectDir, name)) };

    internal sealed record SavedWrapper(VideoSource Source);

    [SuppressResourceClassGeneration]
    internal sealed class SavedMemberHolder : EngineObject
    {
        public SavedMemberHolder() => ScanProperties<SavedMemberHolder>();

        public object? Saved { get; init; }

        [NotAutoSerialized]
        public IExpressionProperty<VideoSource?> Unsaved { get; } = Property.CreateExpression<VideoSource?>();

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Saved), Saved);
        }
    }
}

// A plugin transition can draw with a file of its own; no built-in transition holds one.
internal sealed partial class ImageMaskTransition : ClipTransition
{
    public ImageMaskTransition() => ScanProperties<ImageMaskTransition>();

    public IExpressionProperty<ImageSource?> Mask { get; } = Property.CreateExpression<ImageSource?>();
}
