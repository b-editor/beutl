using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor;

// The .scene is shared between machines (Git, sync folders), so its media fingerprints must change only
// when a file's length or content changes, never because a file's timestamp differs.
[TestFixture]
public class MediaFingerprintPersistenceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "media-fingerprints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task ForcedPass_AfterOnlyATimestampChange_LeavesTheSceneFileUnchanged()
    {
        (Scene scene, string media) = CreateSceneWithImage();
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        byte[] saved = File.ReadAllBytes(scene.Uri!.LocalPath);

        File.SetLastWriteTimeUtc(media, File.GetLastWriteTimeUtc(media).AddHours(-3));
        await service.UpdateFingerprintsAsync(scene, force: true);
        // The editor's background patch after an explicit save, then the next explicit save.
        bool patched = CoreSerializer.UpdateStoredMetadata(
            scene.Uri, scene.Id, nameof(Scene.MediaFingerprints),
            JsonSerializer.SerializeToNode(Scene.ToSerializedMediaFingerprints(scene.MediaFingerprints, scene.Uri)));
        byte[] afterPatch = File.ReadAllBytes(scene.Uri.LocalPath);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(patched, Is.False, "A changed patch is what requests a version control snapshot.");
            Assert.That(afterPatch, Is.EqualTo(saved));
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(saved));
        });
    }

    [Test]
    public async Task FirstFingerprintPatch_PutsTheKeyWhereTheNextFullSaveWritesIt()
    {
        // An explicit save writes the scene before its media are hashed; the background patch then adds
        // the fingerprints. The next explicit save must not move them, or it changes the file again.
        (Scene scene, _) = CreateSceneWithImage();
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);

        bool patched = CoreSerializer.UpdateStoredMetadata(
            scene.Uri, scene.Id, nameof(Scene.MediaFingerprints),
            JsonSerializer.SerializeToNode(Scene.ToSerializedMediaFingerprints(scene.MediaFingerprints, scene.Uri)));
        byte[] afterPatch = File.ReadAllBytes(scene.Uri.LocalPath);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(patched, Is.True);
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(afterPatch));
        });
    }

    [Test]
    public void BothWriters_WriteFingerprintsWithoutTimestampsInOrdinalKeyOrder()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        // Arrive in reverse order, as a scan can produce them.
        scene.MediaFingerprints[new Uri(Path.Combine(_root, "b.png")).AbsoluteUri] = new MediaFileFingerprint(2, 222, "BB");
        scene.MediaFingerprints[new Uri(Path.Combine(_root, "a.png")).AbsoluteUri] = new MediaFileFingerprint(1, 111, "AA");
        scene.MediaFingerprints[new Uri(Path.Combine(_root, "B.png")).AbsoluteUri] = new MediaFileFingerprint(3, 333, "CC");

        CoreSerializer.StoreToUri(scene, sceneUri);
        JsonObject fullSave = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))![nameof(Scene.MediaFingerprints)]!.AsObject();
        JsonObject patch = JsonSerializer.SerializeToNode(
            Scene.ToSerializedMediaFingerprints(scene.MediaFingerprints, sceneUri))!.AsObject();

        string[] expected = ["B.png", "a.png", "b.png"];
        Assert.Multiple(() =>
        {
            Assert.That(fullSave.Select(pair => pair.Key), Is.EqualTo(expected));
            Assert.That(patch.Select(pair => pair.Key), Is.EqualTo(expected));
            Assert.That(fullSave.Select(pair => pair.Value!.AsObject().ContainsKey("LastWriteTimeUtcTicks")), Has.All.False);
            Assert.That(patch.Select(pair => pair.Value!.AsObject().ContainsKey("LastWriteTimeUtcTicks")), Has.All.False);
            Assert.That(fullSave["a.png"]![nameof(MediaFileFingerprint.Sha256)]!.GetValue<string>(), Is.EqualTo("AA"));
            Assert.That(fullSave["a.png"]![nameof(MediaFileFingerprint.Length)]!.GetValue<long>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void SceneWithTimestampsFromAnEarlierBuild_StillLoads()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        CoreSerializer.StoreToUri(scene, sceneUri);
        JsonObject json = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
        json[nameof(Scene.MediaFingerprints)] = new JsonObject
        {
            ["media.png"] = new JsonObject
            {
                [nameof(MediaFileFingerprint.Length)] = 42,
                ["LastWriteTimeUtcTicks"] = 638000000000000000,
                [nameof(MediaFileFingerprint.Sha256)] = "ABCDEF",
            },
        };
        File.WriteAllText(sceneUri.LocalPath, json.ToJsonString());

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        MediaFileFingerprint fingerprint = restored.MediaFingerprints[new Uri(Path.Combine(_root, "media.png")).AbsoluteUri];

        Assert.Multiple(() =>
        {
            Assert.That(fingerprint.Length, Is.EqualTo(42));
            Assert.That(fingerprint.Sha256, Is.EqualTo("ABCDEF"));
            Assert.That(fingerprint.LastWriteTimeUtcTicks, Is.Zero, "The saved time is not used, so it is not read either.");
        });
    }

    [Test]
    public void EarlierBuilds_ReadFingerprintsWithoutTimestamps()
    {
        // Builds before this change declare the time as a constructor parameter. With the options the
        // serializer uses, a missing value reads as 0 instead of failing.
        JsonNode saved = JsonSerializer.SerializeToNode(new MediaFileFingerprint(42, 123, "ABCDEF"))!;

        EarlierMediaFileFingerprint? earlier = saved.Deserialize<EarlierMediaFileFingerprint>(JsonHelper.SerializerOptions);

        Assert.That(earlier, Is.EqualTo(new EarlierMediaFileFingerprint(42, 0, "ABCDEF")));
    }

    // The shape of MediaFileFingerprint in the 2.x builds that saved LastWriteTimeUtcTicks.
    private sealed record EarlierMediaFileFingerprint(long Length, long LastWriteTimeUtcTicks, string Sha256);

    private (Scene Scene, string Media) CreateSceneWithImage()
    {
        string media = Path.Combine(_root, "image.png");
        using (var bitmap = new Bitmap(4, 4))
        {
            bitmap.GetPixelSpan().Fill(255);
            Assert.That(bitmap.Save(media, EncodedImageFormat.Png), Is.True);
        }

        var source = new ImageSource();
        source.ReadFrom(new Uri(media));
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(Path.Combine(_root, "scene.scene")) };
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(_root, "image.belm")),
        };
        element.Objects.Add(new SourceImage { Source = { CurrentValue = source } });
        scene.Children.Add(element);
        return (scene, media);
    }
}
