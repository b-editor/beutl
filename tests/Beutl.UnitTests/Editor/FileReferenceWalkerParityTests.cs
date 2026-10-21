using Beutl.Animation;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Transitions;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

// Every walker that lists a document's files must find a file wherever the document can hold one. ProxySourceEnumerator
// is left out on purpose: it lists what the renderer opens, so it skips what the render path does not draw.
[TestFixture]
public class FileReferenceWalkerParityTests
{
    public enum Place { Property, KeyFrameValue, NodePort, Transition, ReferencedScene }

    public enum Walker { ExternalResourceCollector, MissingMediaFileUris, SerializedGraphTraversal }

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
        if (place == Place.NodePort && walker == Walker.SerializedGraphTraversal)
            Assert.Ignore("This walker does not read node-port values: #3277.");

        Scene scene = CreateScene("main.scene");
        Uri expected = AddFileIn(scene, place);

        Assert.That(Find(walker, scene).Distinct(), Is.EqualTo(new[] { expected }));
    }

    private IEnumerable<Uri> Find(Walker walker, Scene scene) => walker switch
    {
        // Lists only files outside the project folder, where the media of this test are.
        Walker.ExternalResourceCollector => ExternalResourceCollector.Collect(scene, _projectDir).FileSources
            .Select(source => source.OriginalUri),
        Walker.MissingMediaFileUris => MissingMediaService.GetFileUris(scene),
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
                animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(1), Value = Open<VideoSource>(uri) });
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
            case Place.Transition:
                var transition = new ImageMaskTransition();
                transition.Mask.Value = Open<ImageSource>(uri);
                element = ElementWith(new SourceImage());
                element.EnterTransition = transition;
                break;
            default:
                Scene referenced = CreateScene("referenced.scene");
                var nested = new SourceVideo();
                nested.Source.Value = Open<VideoSource>(uri);
                referenced.Children.Add(ElementWith(nested));
                var drawable = new SceneDrawable();
                drawable.ReferencedScene.Value = referenced;
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
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(_projectDir, $"{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(obj);
        return element;
    }

    private Scene CreateScene(string name)
        => new(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_projectDir, name)) };
}

// A plugin transition can draw with a file of its own; no built-in transition holds one.
internal sealed partial class ImageMaskTransition : ClipTransition
{
    public ImageMaskTransition() => ScanProperties<ImageMaskTransition>();

    public IExpressionProperty<ImageSource?> Mask { get; } = Property.CreateWithExpression<ImageSource?>();
}
