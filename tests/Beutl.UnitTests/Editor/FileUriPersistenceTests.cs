using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class FileUriPersistenceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("beutl-file-uri-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase("scene#1")]
    [TestCase("scene%41")]
    [TestCase("two words")]
    [TestCase("scene #1 %20")]
    public void GeneratedElementUris_PreserveTheSceneDirectory(string name)
    {
        string directory = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        Uri sceneUri = UriHelper.CreateFromPath(Path.Combine(directory, name + ".scene"));
        Guid id = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(ElementFileNaming.GetUri(sceneUri, id).LocalPath,
                Is.EqualTo(Path.Combine(directory, $"{id:N}.belm")));
            Assert.That(Path.GetDirectoryName(RandomFileNameGenerator.GenerateUri(directory, "belm").LocalPath),
                Is.EqualTo(directory));
            Assert.That(Path.GetDirectoryName(RandomFileNameGenerator.GenerateUri(sceneUri, "belm").LocalPath),
                Is.EqualTo(directory));
        });
    }

    [TestCase("scene#1")]
    [TestCase("scene%41")]
    [TestCase("two words")]
    [TestCase("scene #1 %20")]
    public void SceneSaveAndFingerprintUpdate_PreserveSidecarReferences(string name)
    {
        string directory = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        var scene = new Scene(64, 64, name)
        {
            Uri = UriHelper.CreateFromPath(Path.Combine(directory, name + ".scene")),
        };
        Uri elementUri = UriHelper.CreateFromPath(Path.Combine(directory, name + ".belm"));
        scene.Children.Add(new Element { Uri = elementUri, Length = { Value = MediaTime.FromSeconds(1) } });
        string mediaUri = UriHelper.CreateFromPath(Path.Combine(directory, name + ".png")).AbsoluteUri;
        scene.SetMediaFingerprints(new Dictionary<string, MediaFileFingerprint>
        {
            [mediaUri] = new(1, "AA"),
        });

        CoreSerializer.StoreToUri(scene, scene.Uri);
        string fingerprintPath = Path.Combine(directory, "media-fingerprints.json");
        Assert.That(File.Exists(fingerprintPath), Is.True);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Assert.That(restored.Children.Single().Uri!.LocalPath, Is.EqualTo(elementUri.LocalPath));
        Assert.That(restored.MediaFingerprints[mediaUri], Is.EqualTo(new MediaFileFingerprint(1, "AA")));

        Assert.That(MediaFingerprintFile.Update(scene.Uri, scene.Id.Value,
            new Dictionary<string, MediaFileFingerprint> { [mediaUri] = new(2, "BB") }), Is.True);
        restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Assert.That(restored.MediaFingerprints[mediaUri], Is.EqualTo(new MediaFileFingerprint(2, "BB")));
    }
}
