using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

// A scene's media fingerprints are shared between machines (Git, sync folders) in a file of their own in the
// scene's folder, which the .scene file references while the scene has any. They must change only when a file's
// length or content changes, never because a file's timestamp differs, and the .scene file holds none of them.
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
        string[] replacedByTheUpdate;
        using (StorageWriteTransaction.InjectFaultsForTesting((step, file) =>
               {
                   if (step == StorageWriteStep.Replace) replaced.Add(file);
               }))
        {
            // The editor's background update after an explicit save, then the next explicit save.
            updated = MediaFingerprintFile.Update(scene.Uri, scene.Id, scene.MediaFingerprints);
            replacedByTheUpdate = [.. replaced];
            CoreSerializer.StoreToUri(scene, scene.Uri);
        }

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.False, "A changed update is what requests a version control snapshot.");
            Assert.That(replacedByTheUpdate, Is.Empty, "The update replaces no file at all.");
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
        scene.SetMediaFingerprints(new Dictionary<string, MediaFileFingerprint>
        {
            [MediaUri("model.gltf")] = new MediaFileFingerprint(4, 444, "DD")
            {
                Dependencies = new(StringComparer.Ordinal)
                {
                    ["textures/z.png"] = new MediaFileFingerprint(6, 666, "FF"),
                    ["model.bin"] = new MediaFileFingerprint(5, 555, "EE"),
                },
            },
            [MediaUri("b.png")] = new MediaFileFingerprint(2, 222, "BB"),
            [MediaUri("a.png")] = new MediaFileFingerprint(1, 111, "AA"),
            [MediaUri("B.png")] = new MediaFileFingerprint(3, 333, "CC"),
        });

        CoreSerializer.StoreToUri(scene, sceneUri);
        string path = MediaFingerprintFile.GetPath(sceneUri.LocalPath);
        string text = File.ReadAllText(path);
        JsonObject file = JsonNode.Parse(text)!.AsObject();
        JsonObject fingerprints = file["Fingerprints"]!.AsObject();
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        MediaFileFingerprint model = restored.MediaFingerprints[MediaUri("model.gltf")];
        JsonObject sceneFile = ReadJson(sceneUri.LocalPath);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(Path.Combine(_root, "media-fingerprints.json")));
            Assert.That(sceneFile.ContainsKey("MediaFingerprints"), Is.False);
            Assert.That(sceneFile.Select(pair => pair.Key).TakeLast(3), Is.EqualTo(new[] { "MediaFingerprintFile", "$type", "minAppVersion" }));
            Assert.That(sceneFile["MediaFingerprintFile"]!.GetValue<string>(), Is.EqualTo("media-fingerprints.json"));
            Assert.That(file.Select(pair => pair.Key), Is.EqualTo(new[] { "Version", "Fingerprints", "$type" }));
            Assert.That(file["Version"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(file["$type"]!.GetValue<string>(), Is.EqualTo("[Beutl.ProjectSystem]:MediaFingerprintFile"));
            Assert.That(fingerprints.Select(pair => pair.Key), Is.EqualTo(new[] { "B.png", "a.png", "b.png", "model.gltf" }));
            Assert.That(fingerprints["model.gltf"]!["Dependencies"]!.AsObject().Select(pair => pair.Key),
                Is.EqualTo(new[] { "model.bin", "textures/z.png" }));
            Assert.That(text, Does.Not.Contain("LastWriteTimeUtcTicks"));
            Assert.That(text, Does.StartWith(
                "{\n  \"Version\": 1,\n  \"Fingerprints\": {\n    \"B.png\": {\n      \"Length\": 3,\n      \"Sha256\": \"CC\"\n    },"));
            Assert.That(restored.MediaFingerprints, Has.Count.EqualTo(4));
            Assert.That(restored.MediaFingerprints[MediaUri("a.png")], Is.EqualTo(new MediaFileFingerprint(1, 0, "AA")));
            Assert.That((model.Length, model.Sha256), Is.EqualTo((4L, "DD")));
            Assert.That(model.Dependencies!["model.bin"], Is.EqualTo(new MediaFileFingerprint(5, 0, "EE")));
            Assert.That(model.Dependencies["textures/z.png"], Is.EqualTo(new MediaFileFingerprint(6, 0, "FF")));
        });
    }

    [Test]
    public void ASceneWithoutFingerprints_HasNoFingerprintFileAndNoReference()
    {
        (Scene scene, _) = CreateSceneWithImage(_root);

        CoreSerializer.StoreToUri(scene, scene.Uri!);
        JsonObject embedded = CoreSerializer.SerializeToJsonObject(scene, new CoreSerializerOptions
        {
            BaseUri = scene.Uri,
            Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
        });
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(scene.MediaFingerprintFile, Is.Null);
            Assert.That(ReadJson(scene.Uri!.LocalPath).ContainsKey("MediaFingerprintFile"), Is.False);
            Assert.That(File.Exists(MediaFingerprintFile.GetPath(scene.Uri.LocalPath)), Is.False);
            Assert.That(embedded.ContainsKey("MediaFingerprintFile"), Is.False);
            Assert.That(restored.MediaFingerprintFile, Is.Null);
            Assert.That(restored.MediaFingerprints, Is.Empty);
        });
    }

    [Test]
    public async Task BackgroundUpdate_WritesNothingWithoutAReference_AndTheNextSaveWritesTheReferenceAndTheFile()
    {
        // An explicit save writes the scene before its media are hashed, so the scene file references no
        // fingerprint file. The scan after it keeps the fingerprints in the scene, and the next save writes them.
        (Scene scene, string media) = CreateSceneWithImage(_root);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] savedScene = File.ReadAllBytes(scene.Uri.LocalPath);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);
        bool keptInTheScene = scene.MediaFingerprintFile is not null;

        bool updated = MediaFingerprintFile.Update(scene.Uri, scene.Id, scene.MediaFingerprints);
        bool writtenByUpdate = File.Exists(path);
        byte[] sceneAfterUpdate = File.ReadAllBytes(scene.Uri.LocalPath);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        Scene reopened = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(keptInTheScene, Is.True);
            Assert.That(updated, Is.False);
            Assert.That(writtenByUpdate, Is.False);
            Assert.That(sceneAfterUpdate, Is.EqualTo(savedScene));
            Assert.That(ReadJson(scene.Uri.LocalPath)["MediaFingerprintFile"]!.GetValue<string>(), Is.EqualTo("media-fingerprints.json"));
            Assert.That(ReadFingerprints(path).Select(pair => pair.Key), Is.EqualTo(new[] { "image.png" }));
            Assert.That(reopened.MediaFingerprints.Keys, Is.EqualTo(new[] { new Uri(media).AbsoluteUri }));
        });
    }

    [Test]
    public async Task BackgroundUpdate_RewritesOnlyTheFileTheSceneFileReferences_AndTheNextSaveKeepsBothFiles()
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] savedScene = File.ReadAllBytes(scene.Uri.LocalPath);
        string savedHash = ReadFingerprints(path)["image.png"]!["Sha256"]!.GetValue<string>();
        // The media file changes on disk, and a scan hashes it again.
        byte[] content = File.ReadAllBytes(media);
        content[^1] ^= 0xff;
        File.WriteAllBytes(media, content);
        await service.UpdateFingerprintsAsync(scene, force: true);

        bool updated = MediaFingerprintFile.Update(scene.Uri, scene.Id, scene.MediaFingerprints);
        byte[] afterUpdate = File.ReadAllBytes(path);
        byte[] sceneAfterUpdate = File.ReadAllBytes(scene.Uri.LocalPath);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.True);
            Assert.That(ReadFingerprints(path)["image.png"]!["Sha256"]!.GetValue<string>(), Is.Not.EqualTo(savedHash));
            Assert.That(sceneAfterUpdate, Is.EqualTo(savedScene), "The update writes only the fingerprint file.");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(afterUpdate));
            Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(savedScene));
        });
    }

    [Test]
    public async Task RemovingTheLastFingerprint_DropsTheReferenceAndDeletesTheFileOnTheNextSave()
    {
        (Scene scene, _) = CreateSceneWithImage(_root);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        bool savedWithFile = File.Exists(path) && ReadJson(scene.Uri.LocalPath).ContainsKey("MediaFingerprintFile");

        // The clip goes, and the next scan drops the fingerprint of its media.
        scene.Children.Clear();
        await service.UpdateFingerprintsAsync(scene, force: true);
        bool keptAfterTheScan = scene.MediaFingerprintFile is not null;
        CoreSerializer.StoreToUri(scene, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(savedWithFile, Is.True);
            Assert.That(keptAfterTheScan, Is.False, "The last fingerprint takes the scene's fingerprint file object with it.");
            Assert.That(ReadJson(scene.Uri.LocalPath).ContainsKey("MediaFingerprintFile"), Is.False);
            Assert.That(File.Exists(path), Is.False, "No orphan stays in the scene's folder.");
        });
    }

    [Test]
    public async Task FingerprintsADevelopmentBuildKeptInTheSceneFile_AreIgnored_AndTheNextSaveDropsThem()
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        // As development builds since #2666 wrote it, here with the time that #2963 stopped saving.
        WriteDevelopmentBuildScene(scene.Uri!, new JsonObject
        {
            ["image.png"] = new JsonObject
            {
                [nameof(MediaFileFingerprint.Length)] = 42,
                ["LastWriteTimeUtcTicks"] = 638000000000000000,
                [nameof(MediaFileFingerprint.Sha256)] = "ABCDEF",
            },
        });
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        int loaded = restored.MediaFingerprints.Count;
        await new MissingMediaService().UpdateFingerprintsAsync(restored, force: true);
        // The scene file references no fingerprint file yet, so the editor's background update writes none.
        bool updated = MediaFingerprintFile.Update(scene.Uri, restored.Id, restored.MediaFingerprints);
        bool writtenByUpdate = File.Exists(path);
        CoreSerializer.StoreToUri(restored, scene.Uri);
        JsonObject saved = ReadJson(scene.Uri.LocalPath);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Children, Has.Count.EqualTo(1));
            Assert.That(loaded, Is.Zero, "Nothing is migrated from the scene file.");
            Assert.That(updated, Is.False);
            Assert.That(writtenByUpdate, Is.False);
            Assert.That(saved.ContainsKey("MediaFingerprints"), Is.False, "A save writes only the keys a scene knows.");
            Assert.That(saved["MediaFingerprintFile"]!.GetValue<string>(), Is.EqualTo("media-fingerprints.json"));
            Assert.That(ReadFingerprints(path)["image.png"]!["Sha256"]!.GetValue<string>(),
                Is.EqualTo(restored.MediaFingerprints[new Uri(media).AbsoluteUri].Sha256),
                "The next scan of the media hashes them again.");
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

    [TestCase("conflict markers")]
    [TestCase("no version")]
    [TestCase("version as text")]
    public async Task AnUnreadableFingerprintFile_DoesNotStopTheSceneFromLoading_AndTheNextSaveReplacesIt(string content)
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        var entries = (JsonObject)ReadFingerprints(path).DeepClone();
        File.WriteAllText(path, content switch
        {
            // As a merge can leave it.
            "conflict markers" => Conflict,
            // The same entries without the version around them.
            "no version" => entries.ToJsonString(),
            _ => new JsonObject { ["Version"] = "1", ["Fingerprints"] = entries }.ToJsonString(),
        });

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        int loaded = restored.MediaFingerprints.Count;
        await service.UpdateFingerprintsAsync(restored, force: true);
        CoreSerializer.StoreToUri(restored, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Children, Has.Count.EqualTo(1));
            Assert.That(loaded, Is.Zero);
            Assert.That(ReadJson(path)["Version"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(ReadFingerprints(path)["image.png"]!["Sha256"]!.GetValue<string>(),
                Is.EqualTo(scene.MediaFingerprints[new Uri(media).AbsoluteUri].Sha256));
        });
    }

    [Test]
    public async Task AFingerprintFileWithALaterVersion_IsNeitherUsedNorRewrittenNorDeleted()
    {
        (Scene scene, _) = CreateSceneWithImage(_root);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        // What a later version of Beutl could write: a format this version does not know.
        JsonObject later = ReadJson(path);
        later["Version"] = 2;
        later["Moved"] = new JsonObject { ["clip.mp4"] = "footage/clip.mp4" };
        File.WriteAllText(path, later.ToJsonString(JsonHelper.SerializerOptions));
        byte[] written = File.ReadAllBytes(path);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        int loaded = restored.MediaFingerprints.Count;
        bool kept = restored.MediaFingerprintFile is { IsFromLaterVersion: true };
        await service.UpdateFingerprintsAsync(restored, force: true);
        int afterTheScan = restored.MediaFingerprints.Count;
        // The background update and a save after the media are hashed again, then a save with none left.
        bool updated = MediaFingerprintFile.Update(scene.Uri, restored.Id, restored.MediaFingerprints);
        CoreSerializer.StoreToUri(restored, scene.Uri);
        restored.SetMediaFingerprints([]);
        CoreSerializer.StoreToUri(restored, scene.Uri);
        // An embedded copy, as in the agent toolkit's snapshots, keeps the version and nothing else.
        JsonObject embedded = CoreSerializer.SerializeToJsonObject(restored, new CoreSerializerOptions
        {
            BaseUri = scene.Uri,
            Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
        })["MediaFingerprintFile"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Zero);
            Assert.That(kept, Is.True);
            Assert.That(afterTheScan, Is.Zero, "The scene uses none of the fingerprints of such a file.");
            Assert.That(updated, Is.False);
            Assert.That(ReadJson(scene.Uri.LocalPath)["MediaFingerprintFile"]!.GetValue<string>(), Is.EqualTo("media-fingerprints.json"),
                "The scene file keeps the reference, so the version that wrote the file still finds it.");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(written));
            Assert.That(embedded["Version"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(embedded.ContainsKey("Fingerprints"), Is.False);
        });
    }

    [TestCase("null dependency")]
    [TestCase("null hash")]
    [TestCase("empty hash")]
    public async Task AnIncompleteFingerprint_IsSkippedOnLoading_AndTheNextSaveRewritesTheFile(string gap)
    {
        (Scene scene, string media) = CreateSceneWithImage(_root);
        await new MissingMediaService().UpdateFingerprintsAsync(scene, force: true);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        string path = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        // Valid JSON, as a hand edit or a merge can leave it: null for a model's dependency, or a fingerprint without
        // a hash, directly and one level down.
        (string direct, string nested) = gap switch
        {
            "null dependency" => (
                """{"Length":1,"Sha256":"AA","Dependencies":{"direct.bin":null}}""",
                """{"Length":2,"Sha256":"BB","Dependencies":{"nested.bin":{"Length":3,"Sha256":"CC","Dependencies":{"deeper.bin":null}}}}"""),
            "null hash" => (
                """{"Length":1,"Sha256":null}""",
                """{"Length":2,"Sha256":"BB","Dependencies":{"nested.bin":{"Length":3,"Sha256":null}}}"""),
            _ => (
                """{"Length":1,"Sha256":""}""",
                """{"Length":2,"Sha256":"BB","Dependencies":{"nested.bin":{"Length":3,"Sha256":""}}}"""),
        };
        var fingerprints = (JsonObject)ReadFingerprints(path).DeepClone();
        fingerprints["direct.gltf"] = JsonNode.Parse(direct);
        fingerprints["nested.gltf"] = JsonNode.Parse(nested);
        File.WriteAllText(path, new JsonObject { ["Version"] = 1, ["Fingerprints"] = fingerprints }.ToJsonString());

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        string[] loaded = [.. restored.MediaFingerprints.Keys];
        CoreSerializer.StoreToUri(restored, scene.Uri);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(new[] { new Uri(media).AbsoluteUri }));
            Assert.That(ReadFingerprints(path).Select(pair => pair.Key), Is.EqualTo(new[] { "image.png" }));
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
        if (removeEveryFingerprint)
        {
            scene.SetMediaFingerprints([]);
        }
        else
        {
            scene.SetMediaFingerprints(new Dictionary<string, MediaFileFingerprint>(scene.MediaFingerprints)
            {
                [key] = scene.MediaFingerprints[key] with { Sha256 = "CHANGED" },
            });
        }

        // As EditViewModel.SaveAsync does it: one transaction, the fingerprint file before the scene file, which
        // it stores without SaveReferencedObjects.
        void SaveAsTheEditorDoes()
        {
            scene.SaveMediaFingerprintFile();
            CoreSerializer.StoreToUri(scene, scene.Uri, CoreSerializationMode.Write);
        }

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        using (StorageWriteTransaction.InjectFaultsForTesting((step, file) =>
               {
                   if (step == StorageWriteStep.Replace && file == Path.GetFullPath(scene.Uri.LocalPath))
                       throw new IOException("Injected scene write failure.");
               }))
        {
            Assert.Throws<IOException>(SaveAsTheEditorDoes);
            transaction.Rollback();
        }

        byte[] afterFailure = File.ReadAllBytes(path);
        SaveAsTheEditorDoes();

        Assert.Multiple(() =>
        {
            Assert.That(afterFailure, Is.EqualTo(saved));
            if (removeEveryFingerprint)
                Assert.That(File.Exists(path), Is.False, "A scene without fingerprints has no file.");
            else
                Assert.That(ReadFingerprints(path)["image.png"]!["Sha256"]!.GetValue<string>(), Is.EqualTo("CHANGED"));
        });
    }

    [Test]
    public void AnEmbeddedSceneCarriesItsFingerprints_AndWritesNoFile()
    {
        // As the agent toolkit's documents and snapshot clones serialize a scene and read it back.
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        scene.SetMediaFingerprints(new Dictionary<string, MediaFileFingerprint>
        {
            [MediaUri("a.png")] = new MediaFileFingerprint(1, 111, "AA"),
        });

        JsonObject json = CoreSerializer.SerializeToJsonObject(scene, new CoreSerializerOptions
        {
            BaseUri = sceneUri,
            Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
        });
        JsonObject embedded = json["MediaFingerprintFile"]!.AsObject();
        json["Uri"] = sceneUri.ToString();
        var clone = (Scene)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene), new CoreSerializerOptions
        {
            BaseUri = sceneUri,
            Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects,
        });

        Assert.Multiple(() =>
        {
            Assert.That(embedded["Uri"]!.GetValue<string>(), Is.EqualTo("media-fingerprints.json"));
            Assert.That(embedded["Fingerprints"]!.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "a.png" }));
            Assert.That(clone.MediaFingerprints.Keys, Is.EqualTo(new[] { MediaUri("a.png") }));
            Assert.That(clone.MediaFingerprints[MediaUri("a.png")], Is.EqualTo(new MediaFileFingerprint(1, 0, "AA")));
            Assert.That(Directory.GetFiles(_root), Is.Empty);
        });
    }

    private string MediaUri(string name) => new Uri(Path.Combine(_root, name)).AbsoluteUri;

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static JsonObject ReadFingerprints(string path) => ReadJson(path)["Fingerprints"]!.AsObject();

    // A scene file as development builds since #2666 wrote it: the fingerprints inside, keyed by paths relative to
    // the scene file, and no file of their own.
    private static void WriteDevelopmentBuildScene(Uri sceneUri, JsonObject fingerprints)
    {
        JsonObject json = ReadJson(sceneUri.LocalPath);
        json.Remove("MediaFingerprintFile");
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
        ImageSource source = TestImages.WriteImage(media);
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
