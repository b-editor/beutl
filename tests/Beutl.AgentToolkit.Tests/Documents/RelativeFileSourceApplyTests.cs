using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Documents;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Documents;

// Media URIs are written relative to the owning element's .belm, which Scene stores as a path
// relative to the scene file and may therefore sit in a subdirectory. Every case below keeps the
// .belm in a subdirectory so that resolving against the scene instead of the element is a
// detectable difference rather than the same answer by coincidence.
// KeyFrame values are not covered: every in-tree media property is a non-animatable
// SimpleProperty, so only a plugin could produce a media-valued keyframe.
[TestFixture]
public class RelativeFileSourceApplyTests
{
    [Test]
    public void Write_resolves_a_relative_file_source_uri_nested_under_an_element()
    {
        string dir = CreateWorkspace();
        string subDir = CreateSubDirectory(dir);
        string imagePath = CreateImage(subDir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        Element element = CreateElement(Path.Combine(subDir, "media.belm"), imagePath);
        scene.Children.Add(element);
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(scene);
        adapter.Write(scene, document);

        Assert.That(SourceUriOf(element)?.LocalPath, Is.EqualTo(imagePath));
    }

    [Test]
    public void Write_resolves_a_relative_file_source_uri_on_an_element_subtree()
    {
        string dir = CreateWorkspace();
        string subDir = CreateSubDirectory(dir);
        string imagePath = CreateImage(subDir);
        Element element = CreateElement(Path.Combine(subDir, "media.belm"), imagePath);
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(element);
        adapter.Write(element, document);

        Assert.That(SourceUriOf(element)?.LocalPath, Is.EqualTo(imagePath));
    }

    [Test]
    public void Write_resolves_a_relative_file_source_uri_on_a_newly_inserted_element()
    {
        string dir = CreateWorkspace();
        string subDir = CreateSubDirectory(dir);
        string imagePath = CreateImage(subDir);
        JsonObject document = ReadSceneDocument(dir, subDir, imagePath);

        // The element does not exist on the target scene yet, so it is created detached from the
        // hierarchy and cannot reach its own .belm through HierarchicalParent.
        var target = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        new DocumentAdapter().Write(target, document);

        Assert.That(target.Children, Has.Count.EqualTo(1));
        Assert.That(SourceUriOf(target.Children[0])?.LocalPath, Is.EqualTo(imagePath));
    }

    [Test]
    public void Write_resolves_a_relative_file_source_uri_on_an_object_added_to_an_existing_element()
    {
        string dir = CreateWorkspace();
        string subDir = CreateSubDirectory(dir);
        string imagePath = CreateImage(subDir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        Element element = CreateElement(Path.Combine(subDir, "media.belm"), imagePath);
        scene.Children.Add(element);
        var adapter = new DocumentAdapter();
        JsonObject document = adapter.Read(scene);

        // The element stays Id-matched so only the object is new: it is populated before insertion
        // and cannot reach the element's .belm through HierarchicalParent, while the ambient
        // document root is the scene, whose directory differs from the element's.
        element.Objects.Clear();
        adapter.Write(scene, document);

        Assert.That(SourceUriOf(element)?.LocalPath, Is.EqualTo(imagePath));
    }

    [Test]
    public void Write_resolves_a_relative_file_source_uri_on_a_wholesale_replaced_element_list()
    {
        string dir = CreateWorkspace();
        string subDir = CreateSubDirectory(dir);
        string imagePath = CreateImage(subDir);
        var sceneUri = new Uri(Path.Combine(dir, "Scene.scene"));
        var source = new Scene(1920, 1080, "Scene") { Uri = sceneUri };
        source.Children.Add(CreateElement(Path.Combine(subDir, "media.belm"), imagePath));
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(source);
        foreach (JsonObject item in RequireArray(document, "Elements").OfType<JsonObject>())
        {
            item.Remove(nameof(CoreObject.Id));
        }

        var target = new Scene(1920, 1080, "Scene") { Uri = sceneUri };
        adapter.Write(target, document);

        Assert.That(target.Children, Has.Count.EqualTo(1));
        Assert.That(SourceUriOf(target.Children[0])?.LocalPath, Is.EqualTo(imagePath));
    }

    [Test]
    public void Write_resolves_a_relative_file_source_uri_on_a_wholesale_replaced_object_list()
    {
        string dir = CreateWorkspace();
        string subDir = CreateSubDirectory(dir);
        string imagePath = CreateImage(subDir);
        Element element = CreateElement(Path.Combine(subDir, "media.belm"), imagePath);
        var adapter = new DocumentAdapter();

        JsonObject document = adapter.Read(element);
        // Dropping every Id makes Objects a non-identity array, which routes the apply through the
        // wholesale ReplaceList path instead of the per-item identity path.
        foreach (JsonObject item in RequireArray(document, nameof(Element.Objects)).OfType<JsonObject>())
        {
            item.Remove(nameof(CoreObject.Id));
        }

        adapter.Write(element, document);

        Assert.That(SourceUriOf(element)?.LocalPath, Is.EqualTo(imagePath));
    }

    private static JsonObject ReadSceneDocument(string dir, string subDir, string imagePath)
    {
        var source = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        source.Children.Add(CreateElement(Path.Combine(subDir, "media.belm"), imagePath));
        return new DocumentAdapter().Read(source);
    }

    private static Element CreateElement(string belmPath, string imagePath)
    {
        var source = new ImageSource();
        source.ReadFrom(new Uri(imagePath));
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(belmPath) };
        element.Objects.Add(new SourceImage { Source = { CurrentValue = source } });
        return element;
    }

    private static Uri? SourceUriOf(Element element)
        => element.Objects.OfType<SourceImage>().Single().Source.CurrentValue?.Uri;

    private static JsonArray RequireArray(JsonObject document, string name)
        => document[name] as JsonArray ?? throw new InvalidOperationException($"'{name}' is not an array.");

    private static string CreateImage(string dir)
    {
        string path = Path.Combine(dir, "source.png");
        using var bitmap = new Bitmap(8, 8);
        Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        return path;
    }

    private static string CreateSubDirectory(string dir)
        => Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
