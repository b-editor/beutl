using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor;

// A scene's media fingerprints are shared between machines (Git, sync folders) in a file of their own next to
// the .scene file. They must change only when a file's length or content changes, never because a file's
// timestamp differs, and the .scene file holds none of them.
[TestFixture]
public class MediaFingerprintPersistenceTests
{
    private const string Conflict = "<<<<<<< ours\n{}\n=======\n{}\n>>>>>>> theirs\n";

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
    public async Task ForcedPass_AfterOnlyATimestampChange_ChangesNeitherFile()
    {
        // The fingerprint file is not written at all. The scene file is written again, as every save does,
        // with the same bytes.
        (Scene scene, string media) = CreateSceneWithImage(_root);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] savedScene = File.ReadAllBytes(scene.Uri.LocalPath);
        byte[] savedFingerprints = File.ReadAllBytes(path);

        File.SetLastWriteTimeUtc(media, File.GetLastWriteTimeUtc(media).AddHours(-3));
        await service.UpdateFingerprintsAsync(scene, force: true);
        var replaced = new List<string>();
        bool updated;
        using (StorageWriteTransaction.InjectFaultsForTesting((step, file) =>
               {
                   if (step == StorageWriteStep.Replace) replaced.Add(file);
               }))
        {
            // The editor's background update after an explicit save, then the next explicit save.
            updated = MediaFingerprintFile.Update(scene.Uri, scene.Id, scene.MediaFingerprints);
            CoreSerializer.StoreToUri(scene, scene.Uri);
        }

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.False, "A changed update is what requests a version control snapshot.");
            Assert.That(replaced, Has.No.Member(Path.GetFullPath(path)));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(savedFingerprints));
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(savedScene));
        });
    }

    [Test]
    public void Saving_WritesTheFingerprintsToTheirOwnFile_SortedAndWithoutTimestamps()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        // Arrive in reverse order, as a scan can produce them.
        scene.MediaFingerprints[MediaUri("model.gltf")] = new MediaFileFingerprint(4, 444, "DD")
        {
            Dependencies = new(StringComparer.Ordinal)
            {
                ["textures/z.png"] = new MediaFileFingerprint(6, 666, "FF"),
                ["model.bin"] = new MediaFileFingerprint(5, 555, "EE"),
            },
        };
        scene.MediaFingerprints[MediaUri("b.png")] = new MediaFileFingerprint(2, 222, "BB");
        scene.MediaFingerprints[MediaUri("a.png")] = new MediaFileFingerprint(1, 111, "AA");
        scene.MediaFingerprints[MediaUri("B.png")] = new MediaFileFingerprint(3, 333, "CC");

        CoreSerializer.StoreToUri(scene, sceneUri);
        string path = MediaFingerprintFile.GetPath(sceneUri.LocalPath);
        string text = File.ReadAllText(path);
        JsonObject file = JsonNode.Parse(text)!.AsObject();
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        MediaFileFingerprint model = restored.MediaFingerprints[MediaUri("model.gltf")];

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(Path.Combine(_root, "scene.media-fingerprints.json")));
            Assert.That(ReadJson(sceneUri.LocalPath).ContainsKey("MediaFingerprints"), Is.False);
            Assert.That(file.Select(pair => pair.Key), Is.EqualTo(new[] { "B.png", "a.png", "b.png", "model.gltf" }));
            Assert.That(file["model.gltf"]!["Dependencies"]!.AsObject().Select(pair => pair.Key),
                Is.EqualTo(new[] { "model.bin", "textures/z.png" }));
            Assert.That(text, Does.Not.Contain("LastWriteTimeUtcTicks"));
            Assert.That(text, Does.StartWith("{\n  \"B.png\": {\n    \"Length\": 3,\n    \"Sha256\": \"CC\"\n  },"));
            Assert.That(restored.MediaFingerprints, Has.Count.EqualTo(4));
            Assert.That(restored.MediaFingerprints[MediaUri("a.png")], Is.EqualTo(new MediaFileFingerprint(1, 0, "AA")));
            Assert.That((model.Length, model.Sha256), Is.EqualTo((4L, "DD")));
            Assert.That(model.Dependencies!["model.bin"], Is.EqualTo(new MediaFileFingerprint(5, 0, "EE")));
            Assert.That(model.Dependencies["textures/z.png"], Is.EqualTo(new MediaFileFingerprint(6, 0, "FF")));
        });
    }

    [Test]
    public async Task BackgroundUpdate_WritesTheFileTheNextSaveKeeps()
    {
        // An explicit save writes the scene before its media are hashed; the background update then writes the
        // fingerprints. The next explicit save must leave both files as they are.
        (Scene scene, _) = CreateSceneWithImage(_root);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] savedScene = File.ReadAllBytes(scene.Uri.LocalPath);
        bool existedBeforeHashing = File.Exists(path);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);

        bool updated = MediaFingerprintFile.Update(scene.Uri, scene.Id, scene.MediaFingerprints);
        byte[] afterUpdate = File.ReadAllBytes(path);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(existedBeforeHashing, Is.False, "A scene without fingerprints has no file.");
            Assert.That(updated, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(afterUpdate));
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(savedScene));
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

    [Test]
    public async Task FingerprintsADevelopmentBuildKeptInTheScene_MoveToTheirOwnFileOnTheNextSave()
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);
        MediaFileFingerprint hashed = scene.MediaFingerprints[new Uri(media).AbsoluteUri];
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        WriteDevelopmentBuildScene(scene.Uri!, new JsonObject
        {
            ["image.png"] = new JsonObject { ["Length"] = hashed.Length, ["Sha256"] = hashed.Sha256 },
        });
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] legacyScene = File.ReadAllBytes(scene.Uri.LocalPath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        // The editor's background update finds the same fingerprints once the scene is open.
        bool updated = MediaFingerprintFile.Update(scene.Uri, restored.Id, restored.MediaFingerprints);
        bool createdByUpdate = File.Exists(path);
        byte[] sceneAfterUpdate = File.ReadAllBytes(scene.Uri.LocalPath);
        CoreSerializer.StoreToUri(restored, scene.Uri);
        Scene reopened = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Children, Has.Count.EqualTo(1));
            Assert.That(restored.MediaFingerprints[new Uri(media).AbsoluteUri], Is.EqualTo(hashed with { LastWriteTimeUtcTicks = 0 }));
            Assert.That(updated, Is.False, "Opening the scene changes nothing; its next save moves the fingerprints.");
            Assert.That(createdByUpdate, Is.False);
            Assert.That(sceneAfterUpdate, Is.EqualTo(legacyScene));
            Assert.That(ReadJson(scene.Uri.LocalPath).ContainsKey("MediaFingerprints"), Is.False);
            Assert.That(ReadJson(path)["image.png"]!["Sha256"]!.GetValue<string>(), Is.EqualTo(hashed.Sha256));
            Assert.That(reopened.MediaFingerprints[new Uri(media).AbsoluteUri], Is.EqualTo(hashed with { LastWriteTimeUtcTicks = 0 }));
        });
    }

    [Test]
    public async Task ChangedFingerprintsOfADevelopmentBuildScene_GoToTheirOwnFile()
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        WriteDevelopmentBuildScene(scene.Uri!, new JsonObject
        {
            ["image.png"] = new JsonObject { ["Length"] = 1, ["Sha256"] = "OUTDATED" },
        });
        byte[] legacyScene = File.ReadAllBytes(scene.Uri!.LocalPath);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        await new MissingMediaService().UpdateFingerprintsAsync(restored, force: true);
        string hash = restored.MediaFingerprints[new Uri(media).AbsoluteUri].Sha256;

        bool updated = MediaFingerprintFile.Update(scene.Uri, restored.Id, restored.MediaFingerprints);
        Scene reopened = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(hash, Is.Not.EqualTo("OUTDATED"));
            Assert.That(updated, Is.True);
            Assert.That(ReadJson(MediaFingerprintFile.GetPath(scene.Uri.LocalPath))["image.png"]!["Sha256"]!.GetValue<string>(),
                Is.EqualTo(hash));
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(legacyScene), "Only a save of the scene rewrites the scene file.");
            Assert.That(reopened.MediaFingerprints[new Uri(media).AbsoluteUri].Sha256, Is.EqualTo(hash),
                "The file wins over the outdated copy in the scene file.");
        });
    }

    [Test]
    public async Task RepairTool_MatchesRenamedMediaByContent_FromTheFingerprintFile_AfterTheSceneFolderMoves()
    {
        string original = Path.Combine(_root, "original");
        (Scene scene, string media) = CreateSceneWithImage(original);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        // As on another machine: the folder is copied elsewhere, and there the media file is renamed next to a
        // file of the same size and other content.
        string moved = Path.Combine(_root, "moved");
        CopyDirectory(original, moved);
        string sceneFile = Path.Combine(moved, Path.GetFileName(scene.Uri!.LocalPath));
        string renamed = Path.Combine(moved, "renamed.png");
        File.Move(Path.Combine(moved, Path.GetFileName(media)), renamed);
        byte[] decoy = File.ReadAllBytes(renamed);
        decoy[^1] ^= 0xff;
        File.WriteAllBytes(Path.Combine(moved, "decoy.png"), decoy);

        var service = new MissingMediaService();
        Scene copy = CoreSerializer.RestoreFromUri<Scene>(new Uri(sceneFile));
        MissingMedia missing = (await service.FindMissingAsync(copy)).Single();
        IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>> matches = await service.FindMatchesAsync([missing], moved);

        Assert.Multiple(() =>
        {
            Assert.That(ReadJson(sceneFile).ContainsKey("MediaFingerprints"), Is.False,
                "The fingerprints can only come from the file next to the scene.");
            Assert.That(missing.Fingerprint, Is.Not.Null);
            Assert.That(matches.TryGetValue(missing, out IReadOnlyList<string>? paths) ? paths : null, Is.EqualTo(new[] { renamed }));
        });
    }

    [Test]
    public async Task AnUnreadableFingerprintFile_DoesNotStopTheSceneFromLoading_AndTheNextSaveReplacesIt()
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        // As a merge can leave it.
        File.WriteAllText(path, Conflict);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        int loaded = restored.MediaFingerprints.Count;
        await service.UpdateFingerprintsAsync(restored, force: true);
        CoreSerializer.StoreToUri(restored, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Children, Has.Count.EqualTo(1));
            Assert.That(loaded, Is.Zero);
            Assert.That(ReadJson(path)["image.png"]!["Sha256"]!.GetValue<string>(),
                Is.EqualTo(scene.MediaFingerprints[new Uri(media).AbsoluteUri].Sha256));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AFingerprintWithANullDependency_IsSkippedOnLoading_AndTheNextSaveRewritesTheFile(bool inSceneFile)
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        // Valid JSON, as a hand edit or a merge can leave it: null for a model's dependency, directly and one level down.
        JsonObject fingerprints = ReadJson(path);
        fingerprints["direct.gltf"] = JsonNode.Parse("""{"Length":1,"Sha256":"AA","Dependencies":{"direct.bin":null}}""");
        fingerprints["nested.gltf"] = JsonNode.Parse(
            """{"Length":2,"Sha256":"BB","Dependencies":{"nested.bin":{"Length":3,"Sha256":"CC","Dependencies":{"deeper.bin":null}}}}""");
        if (inSceneFile) WriteDevelopmentBuildScene(scene.Uri, fingerprints);
        else File.WriteAllText(path, fingerprints.ToJsonString());

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        string[] loaded = [.. restored.MediaFingerprints.Keys];
        CoreSerializer.StoreToUri(restored, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(new[] { new Uri(media).AbsoluteUri }));
            Assert.That(ReadJson(path).Select(pair => pair.Key), Is.EqualTo(new[] { "image.png" }));
            Assert.That(ReadJson(scene.Uri.LocalPath).ContainsKey("MediaFingerprints"), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AFailedSave_PutsTheFingerprintFileBack(bool removeEveryFingerprint)
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] saved = File.ReadAllBytes(path);
        string key = new Uri(media).AbsoluteUri;
        if (removeEveryFingerprint) scene.MediaFingerprints.Clear();
        else scene.MediaFingerprints[key] = scene.MediaFingerprints[key] with { Sha256 = "CHANGED" };

        // As EditViewModel.SaveAsync does it: one transaction, with the scene file written last.
        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        using (StorageWriteTransaction.InjectFaultsForTesting((step, file) =>
               {
                   if (step == StorageWriteStep.Replace && file == Path.GetFullPath(scene.Uri.LocalPath))
                       throw new IOException("Injected scene write failure.");
               }))
        {
            Assert.Throws<IOException>(() => CoreSerializer.StoreToUri(scene, scene.Uri, CoreSerializationMode.Write));
            transaction.Rollback();
        }

        byte[] afterFailure = File.ReadAllBytes(path);
        CoreSerializer.StoreToUri(scene, scene.Uri, CoreSerializationMode.Write);

        Assert.Multiple(() =>
        {
            Assert.That(afterFailure, Is.EqualTo(saved));
            if (removeEveryFingerprint)
                Assert.That(File.Exists(path), Is.False, "A scene without fingerprints has no file.");
            else
                Assert.That(ReadJson(path)["image.png"]!["Sha256"]!.GetValue<string>(), Is.EqualTo("CHANGED"));
        });
    }

    // The shape of MediaFileFingerprint in the 2.x builds that saved LastWriteTimeUtcTicks.
    private sealed record EarlierMediaFileFingerprint(long Length, long LastWriteTimeUtcTicks, string Sha256);

    private string MediaUri(string name) => new Uri(Path.Combine(_root, name)).AbsoluteUri;

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    // A scene file as development builds since #2666 wrote it: the fingerprints inside, keyed by paths relative to
    // the scene file, and no file of their own.
    private static void WriteDevelopmentBuildScene(Uri sceneUri, JsonObject fingerprints)
    {
        JsonObject json = ReadJson(sceneUri.LocalPath);
        json["MediaFingerprints"] = fingerprints;
        File.WriteAllText(sceneUri.LocalPath, json.ToJsonString(JsonHelper.SerializerOptions));
        File.Delete(MediaFingerprintFile.GetPath(sceneUri.LocalPath));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private static (Scene Scene, string Media) CreateSceneWithImage(string directory)
    {
        Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, "image.png");
        using (var bitmap = new Bitmap(4, 4))
        {
            bitmap.GetPixelSpan().Fill(255);
            Assert.That(bitmap.Save(media, EncodedImageFormat.Png), Is.True);
        }

        var source = new ImageSource();
        source.ReadFrom(new Uri(media));
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(Path.Combine(directory, "scene.scene")) };
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(directory, "image.belm")),
        };
        element.Objects.Add(new SourceImage { Source = { CurrentValue = source } });
        scene.Children.Add(element);
        return (scene, media);
    }
}
