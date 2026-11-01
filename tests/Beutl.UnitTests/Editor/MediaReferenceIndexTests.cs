using System.Diagnostics;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class MediaReferenceIndexTests
{
    public enum Place { Property, KeyFrameValue, NodePort, NodePortKeyFrame, Transition }

    private static readonly string s_mediaDir = Path.Combine(Path.GetTempPath(), "beutl-media-references");

    [Test]
    public void AnEditWalksOnlyTheElementItChanges([Values] Place place)
    {
        var scene = new Scene();
        scene.Children.Add(VideoElement("first.mp4"));
        (Element holder, Action<Uri> link) = CreateHolder(place);
        scene.Children.Add(holder);
        scene.Children.Add(VideoElement("last.mp4"));
        var index = new MediaReferenceIndex(scene);
        using var observer = Observe(scene, index);
        Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("first.mp4"), MediaUri("last.mp4") }));

        Uri linked = MediaUri(place is Place.Property or Place.Transition ? "linked.png" : "linked.mp4");
        link(linked);

        // A node port holds its animation outside the hierarchy, so an edit of its key frames names no element
        // and every element is walked again.
        int walks = place == Place.NodePortKeyFrame ? 6 : 4;
        Assert.Multiple(() =>
        {
            Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("first.mp4"), linked, MediaUri("last.mp4") }));
            Assert.That(index.ScannedElements, Is.EqualTo(walks));
        });
    }

    [Test]
    public void ElementsAreWalkedWhenTheyJoinTheSceneAndDroppedWhenTheyLeave()
    {
        var scene = new Scene();
        Element first = VideoElement("first.mp4");
        Element second = VideoElement("second.mp4");
        scene.Children.Add(first);
        scene.Children.Add(second);
        var index = new MediaReferenceIndex(scene);
        using var observer = Observe(scene, index);
        Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("first.mp4"), MediaUri("second.mp4") }));

        scene.Children.Add(VideoElement("third.mp4"));
        Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("first.mp4"), MediaUri("second.mp4"), MediaUri("third.mp4") }));
        scene.Children.Move(2, 0);
        Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("third.mp4"), MediaUri("first.mp4"), MediaUri("second.mp4") }));
        scene.Children.Remove(first);
        Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("third.mp4"), MediaUri("second.mp4") }));
        Assert.That(index.ScannedElements, Is.EqualTo(3));

        // An element sends no operations while it is out of the scene. Leaving and joining again between two
        // queries still walks it again.
        scene.Children.Remove(second);
        ((SourceVideo)second.Objects[0]).Source.Value = Open<VideoSource>(MediaUri("replaced.mp4"));
        scene.Children.Add(second);
        Assert.Multiple(() =>
        {
            Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("third.mp4"), MediaUri("replaced.mp4") }));
            Assert.That(index.ScannedElements, Is.EqualTo(4));
        });
    }

    [Test]
    public void ASourceRelinkedInPlaceNeedsNoWalk()
    {
        var scene = new Scene();
        var relinked = Open<VideoSource>(MediaUri("old.mp4"));
        var unset = new ImageSource();
        scene.Children.Add(ElementWith(new SourceVideo { Source = { Value = relinked } }));
        scene.Children.Add(ElementWith(new SourceImage { Source = { Value = unset } }));
        var index = new MediaReferenceIndex(scene);
        using var observer = Observe(scene, index);
        Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("old.mp4") }));

        relinked.ReadFrom(MediaUri("new.mp4"));
        unset.ReadFrom(MediaUri("image.png"));

        Assert.Multiple(() =>
        {
            Assert.That(Files(index), Is.EqualTo(new[] { MediaUri("new.mp4"), MediaUri("image.png") }));
            Assert.That(index.ScannedElements, Is.EqualTo(2));
        });
    }

    [Test]
    public void AnOperationOutsideTheElementsWalksNothingAndOneItCannotPlaceWalksEverything()
    {
        var scene = new Scene();
        scene.Children.Add(VideoElement("first.mp4"));
        scene.Children.Add(VideoElement("second.mp4"));
        var index = new MediaReferenceIndex(scene);
        using var observer = Observe(scene, index);
        index.GetReferences();

        scene.Duration.Value = MediaTime.FromSeconds(10);
        scene.Layers.Add(new TimelineLayer());
        index.GetReferences();
        Assert.That(index.ScannedElements, Is.EqualTo(2));

        index.Invalidate(new CustomOperation(() => { }, () => { }) { SequenceNumber = 0 });
        index.GetReferences();
        Assert.That(index.ScannedElements, Is.EqualTo(4));

        var detached = new SourceImage();
        index.Invalidate(new UpdatePropertyValueOperation<ImageSource?>(detached, "Source", null, null) { SequenceNumber = 0 });
        index.GetReferences();
        Assert.That(index.ScannedElements, Is.EqualTo(6));
    }

    [Test]
    public void ReportsWhatAWalkOfTheWholeSceneReports()
    {
        var scene = new Scene();
        var shared = new FontFamily("Shared");
        scene.Children.Add(ElementWith(new TextBlock { FontFamily = { Value = shared } }));
        scene.Children.Add(VideoElement("clip.mp4"));
        scene.Children.Add(ElementWith(new TextBlock { FontFamily = { Value = shared } }));
        scene.Children.Add(VideoElement("clip.mp4"));
        foreach (Place place in Enum.GetValues<Place>())
        {
            (Element holder, Action<Uri> link) = CreateHolder(place);
            link(MediaUri(place is Place.Property or Place.Transition ? "image.png" : "clip.mp4"));
            scene.Children.Add(holder);
        }

        var sourceWithoutUri = new SourceImage { Source = { Value = new ImageSource() } };
        scene.Children.Add(ElementWith(sourceWithoutUri));
        var index = new MediaReferenceIndex(scene);

        (object Value, Element? Element)[] expected = WalkScene(scene);
        IReadOnlyList<MediaReference> actual = index.GetReferences();
        Assert.That(actual, Has.Count.EqualTo(expected.Length));
        Assert.Multiple(() =>
        {
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].Value, Is.SameAs(expected[i].Value), $"value {i}");
                Assert.That(actual[i].Element, Is.SameAs(expected[i].Element), $"element {i}");
            }
        });
    }

    [Test]
    public void TwentyThousandElements_AnEditWalksOnlyThatElement()
    {
        Scene scene = CreateLargeScene(20000);
        var index = new MediaReferenceIndex(scene);
        using var observer = Observe(scene, index);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        HashSet<(object Source, Uri? Uri)> keys = MissingMediaService.GetReferenceKeys(index.GetReferences());
        double first = watch.Elapsed.TotalMilliseconds;
        long firstBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.That(index.ScannedElements, Is.EqualTo(20000));

        var changed = new List<double>();
        var files = new List<double>();
        long changedBytes = 0;
        for (int i = 0; i < 7; i++)
        {
            Uri linked = MediaUri($"edited{i}.png");
            ((SourceImage)scene.Children[i * 5 + 1].Objects[0]).Source.Value = Open<ImageSource>(linked);

            // What the editor runs for every edit: the reference check, then the auto-save's file list.
            allocated = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            HashSet<(object Source, Uri? Uri)> current = MissingMediaService.GetReferenceKeys(index.GetReferences());
            Assert.That(current.SetEquals(keys), Is.False);
            changed.Add(watch.Elapsed.TotalMilliseconds);
            changedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            keys = current;
            watch.Restart();
            Uri[] uris = MissingMediaService.GetFileUris(index.GetReferences());
            files.Add(watch.Elapsed.TotalMilliseconds);
            Assert.That(uris, Does.Contain(linked));
        }

        Assert.That(index.ScannedElements, Is.EqualTo(20007));
        TestContext.Out.WriteLine($"First query, walking 20000 elements: {first:F1} ms, {firstBytes / 1024 / 1024} MiB. "
            + $"After an edit, reference check median {changed.Order().ElementAt(3):F2} ms, {changedBytes / 1024} KiB; "
            + $"file list median {files.Order().ElementAt(3):F2} ms.");
    }

    // The walk the editor ran for every query before it kept an index.
    private static (object Value, Element? Element)[] WalkScene(Scene scene)
    {
        var result = new List<(object, Element?)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CoreSerializer.Visit(scene, visit =>
        {
            if ((visit.Value is FontFamily || MissingMediaService.GetUri(visit.Value) is { IsFile: true }) && visited.Add(visit.Value))
            {
                Element? element = null;
                for (CoreSerializationVisit? parent = visit.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent.Value is Element owner)
                    {
                        element = owner;
                        break;
                    }
                }

                result.Add((visit.Value, element));
            }

            return false;
        });
        return [.. result];
    }

    // An element with an empty place for a file, and the edit that puts one there.
    private static (Element Element, Action<Uri> Link) CreateHolder(Place place)
    {
        switch (place)
        {
            case Place.Property:
                var image = new SourceImage();
                return (ElementWith(image), uri => image.Source.Value = Open<ImageSource>(uri));
            case Place.KeyFrameValue:
                var video = new SourceVideo();
                var keyFrame = new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(1) } };
                var animation = new KeyFrameAnimation<VideoSource?>();
                animation.KeyFrames.Add(keyFrame);
                video.Source.SetKeyFrameAnimation(animation);
                return (ElementWith(video), uri => keyFrame.Value.Value = Open<VideoSource>(uri));
            case Place.NodePort:
                var node = new VideoSourceNode();
                var graph = new NodeGraphDrawable();
                graph.Model.Value!.Nodes.Add(node);
                return (ElementWith(graph), uri => node.Source.Property!.SetValue(Open<VideoSource>(uri)));
            case Place.NodePortKeyFrame:
                var animatedNode = new VideoSourceNode();
                var portKeyFrame = new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(1) } };
                var portAnimation = new KeyFrameAnimation<VideoSource?>();
                portAnimation.KeyFrames.Add(portKeyFrame);
                ((IAnimatablePropertyAdapter)animatedNode.Source.Property!).Animation = portAnimation;
                var animatedGraph = new NodeGraphDrawable();
                animatedGraph.Model.Value!.Nodes.Add(animatedNode);
                return (ElementWith(animatedGraph), uri => portKeyFrame.Value.Value = Open<VideoSource>(uri));
            default:
                var transition = new ImageMaskTransition();
                Element element = ElementWith(new SourceImage());
                element.EnterTransition.Value = transition;
                return (element, uri => transition.Mask.Value = Open<ImageSource>(uri));
        }
    }

    private static Scene CreateLargeScene(int count)
    {
        var scene = new Scene();
        for (int i = 0; i < count; i++)
        {
            // 500 files of each kind, each used by eight elements.
            EngineObject obj = (i % 5) switch
            {
                0 => new SourceVideo { Source = { Value = Open<VideoSource>(MediaUri($"clip{i / 5 % 500}.mp4")) } },
                1 => new SourceImage { Source = { Value = Open<ImageSource>(MediaUri($"image{i / 5 % 500}.png")) } },
                2 => new SourceSound { Source = { Value = Open<SoundSource>(MediaUri($"sound{i / 5 % 500}.wav")) } },
                3 => new TextBlock { Text = { Value = $"Caption {i}" }, FontFamily = { Value = new FontFamily($"Family {i % 20}") } },
                _ => new RectShape(),
            };
            if (obj is Drawable drawable)
            {
                ((TransformGroup)drawable.Transform.Value!).Children.Add(new TranslateTransform { X = { Value = i } });
                ((FilterEffectGroup)drawable.FilterEffect.Value!).Children.Add(new Blur { Sigma = { Value = new Size(2, 2) } });
            }

            Element element = ElementWith(obj);
            element.Start.Value = MediaTime.FromSeconds(i / 10 * 2);
            element.ZIndex.Value = i % 10;
            scene.Children.Add(element);
        }

        return scene;
    }

    private static CoreObjectOperationObserver Observe(Scene scene, MediaReferenceIndex index)
    {
        var observer = new CoreObjectOperationObserver(null, scene, new OperationSequenceGenerator());
        observer.Operations.Subscribe(index.Invalidate);
        return observer;
    }

    private static Uri[] Files(MediaReferenceIndex index) => MissingMediaService.GetFileUris(index.GetReferences());

    private static Element VideoElement(string file)
        => ElementWith(new SourceVideo { Source = { Value = Open<VideoSource>(MediaUri(file)) } });

    private static Element ElementWith(EngineObject obj)
    {
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(obj);
        return element;
    }

    private static Uri MediaUri(string file) => new(Path.Combine(s_mediaDir, file));

    private static T Open<T>(Uri uri) where T : IFileSource, new()
    {
        var source = new T();
        source.ReadFrom(uri);
        return source;
    }
}
