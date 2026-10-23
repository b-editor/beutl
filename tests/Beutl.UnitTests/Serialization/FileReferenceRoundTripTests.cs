using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

// A file named with "#", "?", "%XX", a space or non-ASCII is read back as the same file from every reference form (#3134).
[TestFixture]
public class FileReferenceRoundTripTests
{
    private static readonly string[] s_names = ["a#b", "a?b", "50%20off", "a%41", "ア50%20off", "two words", "シーン", "シーン #1"];

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl_references_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    // Duplicate, split and paste write the copy without a base, so its media references are absolute.
    [TestCaseSource(nameof(s_names))]
    public void A_copied_clip_keeps_its_media(string name)
    {
        Element element = ElementWithImage(name, out string imagePath);

        ObjectRegenerator.Regenerate(element, out Element copy);

        Assert.That(ImagePath(copy), Is.EqualTo(imagePath));
    }

    [TestCaseSource(nameof(s_names))]
    public void A_media_reference_resolves_against_an_explicit_file_base(string name)
    {
        Element element = ElementWithImage(name, out string imagePath);
        CoreSerializer.StoreToUri(element, element.Uri!);
        JsonObject json = JsonNode.Parse(File.ReadAllText(element.Uri!.LocalPath))!.AsObject();
        var read = new Element();

        CoreSerializer.PopulateFromJsonObject(read, json, new CoreSerializerOptions { BaseUri = new Uri(element.Uri.AbsoluteUri) });

        Assert.That(ImagePath(read), Is.EqualTo(imagePath));
    }

    [TestCaseSource(nameof(s_names))]
    public void A_media_reference_resolves_against_a_path_base(string name)
    {
        Element element = ElementWithImage(name, out string imagePath);
        CoreSerializer.StoreToUri(element, element.Uri!);

        Element read = CoreSerializer.RestoreFromUri<Element>(new Uri(element.Uri!.LocalPath));

        Assert.That(ImagePath(read), Is.EqualTo(imagePath));
    }

    // A scene on another drive is referenced absolutely, as every reference is when written without a base.
    [TestCaseSource(nameof(s_names))]
    public void An_absolute_scene_reference_keeps_the_scene_file(string name)
    {
        string scenePath = CreatePath(Path.Combine(name, $"{name}.scene"));
        var scene = new Scene { Uri = FileUri(scenePath) };
        CoreSerializer.StoreToUri(scene, scene.Uri);
        var project = new Project();
        project.Items.Add(scene);

        JsonObject json = CoreSerializer.SerializeToJsonObject(project);
        var read = (Project)CoreSerializer.DeserializeFromJsonObject(json, typeof(Project));

        Assert.That(read.Items.Single().Uri!.LocalPath, Is.EqualTo(scenePath));
    }

    [TestCaseSource(nameof(s_names))]
    public void An_absolute_element_reference_keeps_the_element_file(string name)
    {
        string elementPath = CreatePath(Path.Combine(name, $"{name}.belm"));
        var scene = new Scene();
        scene.Children.Add(new Element { Length = { Value = MediaTime.FromSeconds(1) }, Uri = FileUri(elementPath) });

        JsonObject json = CoreSerializer.SerializeToJsonObject(
            scene, new CoreSerializerOptions { Mode = CoreSerializationMode.EmbedReferencedObjects });
        var read = (Scene)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene), new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
        });

        Assert.That(read.Children.Single().Uri!.LocalPath, Is.EqualTo(elementPath));
    }

    // The clip sits in a plain folder, so only the media file's name is special.
    private Element ElementWithImage(string name, out string imagePath)
    {
        imagePath = CreatePath(Path.Combine("clip", $"{name}.png"));
        File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47]);
        var source = new ImageSource();
        source.ReadFrom(FileUri(imagePath));
        var image = new SourceImage();
        image.Source.Value = source;
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) }, Uri = FileUri(CreatePath(Path.Combine("clip", "clip.belm"))) };
        element.AddObject(image);
        return element;
    }

    private string CreatePath(string relative)
    {
        if (relative.Contains('?') && OperatingSystem.IsWindows())
            Assert.Ignore("A Windows file name cannot hold '?'.");

        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string ImagePath(Element element)
        => element.Objects.OfType<SourceImage>().Single().Source.Value!.Uri.LocalPath;

    // As the editor makes the Uri of a file it opens or creates.
    private static Uri FileUri(string path) => UriHelper.CreateFromPath(path);
}
