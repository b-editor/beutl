using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using NuGet.Versioning;

namespace Beutl.UnitTests.ProjectSystem;

// Every save writes minAppVersion into each root file it writes and appVersion into the project file, one gate
// refuses a root file that needs a newer Beutl, and the project file is raised before any other file of the
// project is written.
[TestFixture]
public class FormatFloorTests
{
    // A project file that an older build saved last.
    private const string OlderAppVersion = "1.1.0";
    private const string OlderMinAppVersion = "1.0.0-preview.9";

    private string _directory = null!;
    private string _projectPath = null!;
    private string _scenePath = null!;
    private string _elementPath = null!;

    public enum RootFile
    {
        Project,
        Scene,
        Element,
    }

    public enum LoneSave
    {
        Scene,
        Element,
        AutoSavedElement,
    }

    public enum LegacyShape
    {
        SceneWithoutDiscriminator,
        ElementWithoutDiscriminator,
        ElementWithOperation,
    }

    // Above the floor and not above this build, as a build that raised the floor records.
    private static string AboveFloor
    {
        get
        {
            NuGetVersion floor = NuGetVersion.Parse(FileFormat.Floor);
            var above = floor.IsPrerelease
                ? new NuGetVersion(floor.Major, floor.Minor, floor.Patch)
                : new NuGetVersion(floor.Major, floor.Minor, floor.Patch + 1);
            Assume.That(above, Is.LessThanOrEqualTo(NuGetVersion.Parse(BeutlApplication.Version)));
            return above.ToNormalizedString();
        }
    }

    private static string AboveRunningVersion
        => new NuGetVersion(NuGetVersion.Parse(BeutlApplication.Version).Major + 1, 0, 0).ToNormalizedString();

    private string[] RootFiles => [_projectPath, _scenePath, _elementPath];

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-format-floor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _projectPath = Path.Combine(_directory, "project.bep");
        _scenePath = Path.Combine(_directory, "scene", "scene.scene");
        _elementPath = Path.Combine(_directory, "scene", "element.belm");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void A_save_writes_the_floor_into_every_root_file_and_the_running_version_into_the_project_file()
    {
        SaveProject();

        Assert.Multiple(() =>
        {
            foreach (string path in RootFiles)
            {
                Assert.That((string?)Read(path)["minAppVersion"], Is.EqualTo(FileFormat.Floor), path);
            }

            Assert.That((string?)Read(_projectPath)["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(Read(_scenePath).ContainsKey("appVersion"), Is.False);
            Assert.That(Read(_elementPath).ContainsKey("appVersion"), Is.False);
        });
    }

    [Test]
    public void A_resave_by_the_build_that_wrote_the_files_changes_no_byte()
    {
        SaveProject();
        byte[][] before = [.. RootFiles.Select(File.ReadAllBytes)];

        Project project = LoadProject();
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.That(RootFiles.Select(File.ReadAllBytes), Is.EqualTo(before));
    }

    // Version control keeps scene and element files unchanged when a newer version saves them unchanged.
    [Test]
    public void A_resave_after_an_update_changes_only_the_app_version_of_the_project_file()
    {
        SaveProject();
        SetVersionFields(_projectPath, "2.0.0-preview.3", FileFormat.Floor);
        JsonObject projectBefore = Read(_projectPath);
        byte[] sceneBefore = File.ReadAllBytes(_scenePath);
        byte[] elementBefore = File.ReadAllBytes(_elementPath);

        Project project = LoadProject();
        CoreSerializer.StoreToUri(project, project.Uri!);
        JsonObject projectAfter = Read(_projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(_scenePath), Is.EqualTo(sceneBefore));
            Assert.That(File.ReadAllBytes(_elementPath), Is.EqualTo(elementBefore));
            Assert.That((string?)projectAfter["appVersion"], Is.EqualTo(BeutlApplication.Version));
            projectAfter["appVersion"] = "2.0.0-preview.3";
            Assert.That(JsonNode.DeepEquals(projectAfter, projectBefore), Is.True);
        });
    }

    [Test]
    public void A_recorded_minimum_above_the_floor_survives_a_save()
    {
        SaveProject();
        string recorded = AboveFloor;
        foreach (string path in RootFiles)
        {
            SetVersionFields(path, recorded, recorded);
        }

        Project project = LoadProject();
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            foreach (string path in RootFiles)
            {
                Assert.That((string?)Read(path)["minAppVersion"], Is.EqualTo(recorded), path);
            }

            Assert.That((string?)Read(_projectPath)["appVersion"], Is.EqualTo(BeutlApplication.Version));
            // Only a project file records it; the next save drops it from the others.
            Assert.That(Read(_scenePath).ContainsKey("appVersion"), Is.False);
            Assert.That(Read(_elementPath).ContainsKey("appVersion"), Is.False);
        });
    }

    [Test]
    public void A_project_file_records_at_least_what_the_files_saved_with_it_require()
    {
        SaveProject();
        string recorded = AboveFloor;
        SetVersionFields(_elementPath, recorded, recorded);

        Project project = LoadProject();
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That((string?)Read(_elementPath)["minAppVersion"], Is.EqualTo(recorded));
            Assert.That((string?)Read(_projectPath)["minAppVersion"], Is.EqualTo(recorded));
            Assert.That(project.MinAppVersion, Is.EqualTo(recorded));
        });
    }

    // An auto-save of a project property writes the project file alone.
    [Test]
    public void A_save_of_the_project_file_alone_records_what_the_files_read_with_it_record()
    {
        SaveProject();
        string recorded = AboveFloor;
        SetVersionFields(_elementPath, null, recorded);

        Project project = LoadProject();
        string loaded = project.MinAppVersion;
        CoreSerializer.StoreToUri(project, project.Uri!, CoreSerializationMode.Write);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(recorded));
            Assert.That((string?)Read(_projectPath)["minAppVersion"], Is.EqualTo(recorded));
        });
    }

    [Test]
    public void A_project_reports_the_version_fields_of_its_file_until_it_is_saved()
    {
        SaveProject();
        SetVersionFields(_projectPath, "2.0.0-preview.3", OlderMinAppVersion);
        // Files that record a minimum would raise the project's.
        SetVersionFields(_scenePath, null, null);
        SetVersionFields(_elementPath, null, null);

        Project project = LoadProject();
        (string AppVersion, string MinAppVersion) loaded = (project.AppVersion, project.MinAppVersion);
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(("2.0.0-preview.3", OlderMinAppVersion)));
            Assert.That(project.AppVersion, Is.EqualTo(BeutlApplication.Version));
            Assert.That(project.MinAppVersion, Is.EqualTo(FileFormat.Floor));
            Assert.That((string?)Read(_projectPath)["appVersion"], Is.EqualTo(BeutlApplication.Version));
        });
    }

    [Test]
    public void Reading_a_root_file_takes_its_version_fields_out_of_the_object()
    {
        SaveProject();
        // A scene or element file can still hold an appVersion, from an earlier development build or a hand edit.
        SetVersionFields(_scenePath, AboveFloor, FileFormat.Floor);
        SetVersionFields(_elementPath, AboveFloor, FileFormat.Floor);
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();

        string projectJson = CoreSerializer.SerializeToJsonObject(project).ToJsonString();
        string embeddedSceneJson = CoreSerializer.SerializeToJsonObject(
            scene,
            new CoreSerializerOptions
            {
                BaseUri = scene.Uri,
                Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
            }).ToJsonString();

        Assert.Multiple(() =>
        {
            foreach (string json in new[] { projectJson, embeddedSceneJson })
            {
                Assert.That(json, Does.Not.Contain("\"appVersion\""));
                Assert.That(json, Does.Not.Contain("\"minAppVersion\""));
            }
        });
    }

    [TestCase(RootFile.Project)]
    [TestCase(RootFile.Scene)]
    [TestCase(RootFile.Element)]
    public void A_root_file_that_needs_a_newer_version_is_refused(RootFile file)
    {
        SaveProject();
        string path = PathOf(file);
        string required = AboveRunningVersion;
        SetVersionFields(path, required, required);
        byte[] before = File.ReadAllBytes(path);

        UnsupportedFileVersionException refused = CatchRefusal(() => LoadProject());

        Assert.Multiple(() =>
        {
            Assert.That(refused.Uri.LocalPath, Is.EqualTo(path));
            Assert.That(refused.RequiredVersion, Is.EqualTo(required));
            Assert.That(refused.Message, Does.Contain(path).And.Contain(required));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        });
    }

    [Test]
    public void A_root_file_recording_a_minimum_that_is_not_a_version_string_is_refused()
    {
        SaveProject();
        // A number no newer than this build, which a released build could not read as a version either.
        JsonObject scene = Read(_scenePath);
        scene["minAppVersion"] = 2;
        scene.JsonSave(_scenePath);

        UnsupportedFileVersionException refused = CatchRefusal(() => LoadProject());

        Assert.Multiple(() =>
        {
            Assert.That(refused.Uri.LocalPath, Is.EqualTo(_scenePath));
            Assert.That(refused.RequiredVersion, Is.EqualTo("2"));
            Assert.That(refused.Message, Does.Contain(_scenePath).And.Contain("cannot read"));
        });
    }

    [Test]
    public void A_refused_file_does_not_become_a_fallback()
    {
        var refused = new UnsupportedFileVersionException(new Uri(_elementPath), AboveRunningVersion, isNewer: true);

        Assert.Multiple(() =>
        {
            Assert.That(
                FallbackDeserializationHelper.TryCreateFallback(typeof(Transform), null, new JsonObject(), refused),
                Is.Null);
            // A file read in parallel arrives wrapped.
            Assert.That(
                FallbackDeserializationHelper.TryCreateFallback(
                    typeof(Transform), null, new JsonObject(), new AggregateException(refused)),
                Is.Null);
        });
    }

    [TestCase(LegacyShape.SceneWithoutDiscriminator)]
    [TestCase(LegacyShape.ElementWithoutDiscriminator)]
    [TestCase(LegacyShape.ElementWithOperation)]
    public void A_1x_project_records_the_floor_after_a_save(LegacyShape shape)
    {
        SaveProject();
        // A 1.x project file records 1.x versions, and its other files record none.
        SetVersionFields(_projectPath, OlderAppVersion, OlderMinAppVersion);
        SetVersionFields(_scenePath, null, null);
        SetVersionFields(_elementPath, null, null);
        switch (shape)
        {
            case LegacyShape.SceneWithoutDiscriminator:
                Edit(_scenePath, json => json.Remove("$type"));
                break;
            case LegacyShape.ElementWithoutDiscriminator:
                Edit(_elementPath, json => json.Remove("$type"));
                break;
            case LegacyShape.ElementWithOperation:
                Edit(_elementPath, json =>
                {
                    json.Remove(nameof(Element.Objects));
                    json["Operation"] = new JsonObject { ["Children"] = new JsonArray() };
                });
                break;
        }

        Project project = LoadProject();
        string loadedMinAppVersion = project.MinAppVersion;
        Element element = ((Scene)project.Items.Single()).Children.Single();
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(loadedMinAppVersion, Is.EqualTo(OlderMinAppVersion));
            Assert.That(element.Objects, Is.Empty);
            foreach (string path in RootFiles)
            {
                JsonObject json = Read(path);
                Assert.That((string?)json["minAppVersion"], Is.EqualTo(FileFormat.Floor), path);
                Assert.That(json["$type"], Is.Not.Null, path);
            }

            Assert.That((string?)Read(_projectPath)["appVersion"], Is.EqualTo(BeutlApplication.Version));
        });
    }

    // #2500: released builds check only the project file, so a file saved on its own has to raise it first.
    [TestCase(LoneSave.Scene)]
    [TestCase(LoneSave.Element)]
    [TestCase(LoneSave.AutoSavedElement)]
    public void Saving_a_file_on_its_own_first_raises_only_the_version_fields_of_the_project_file(LoneSave save)
    {
        SaveProject();
        SetVersionFields(_projectPath, OlderAppVersion, OlderMinAppVersion);
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();
        Element element = scene.Children.Single();
        JsonObject before = Read(_projectPath);

        List<Replacement> replaced = RecordReplacements(() =>
        {
            switch (save)
            {
                case LoneSave.Scene:
                    CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write);
                    break;
                case LoneSave.Element:
                    CoreSerializer.StoreToUri(element, element.Uri!);
                    break;
                case LoneSave.AutoSavedElement:
                    using (var autoSave = new AutoSaveService())
                    {
                        autoSave.SaveObjects([element]);
                    }

                    break;
            }
        });

        AssertProjectFileRaisedFirst(replaced, save == LoneSave.Scene ? _scenePath : _elementPath, before);
    }

    // The case #2500 describes: a file-backed object outside the hierarchy, saved on its own by an auto-save.
    [Test]
    public void A_referenced_file_outside_the_hierarchy_saved_on_its_own_first_raises_the_project_file()
    {
        string referencedPath = Path.Combine(_directory, "scene", "referenced.json");
        SaveProject(new ReferencingElement
        {
            Reference = new ReferencedFile { Uri = new Uri(referencedPath), Text = "before" },
        });
        SetVersionFields(_projectPath, OlderAppVersion, OlderMinAppVersion);
        Project project = LoadProject();
        var element = (ReferencingElement)((Scene)project.Items.Single()).Children.Single();
        ReferencedFile referenced = element.Reference!;
        referenced.Text = "after";
        JsonObject before = Read(_projectPath);

        List<Replacement> replaced = RecordReplacements(() =>
        {
            using var autoSave = new AutoSaveService();
            autoSave.SaveObjects([referenced]);
        });

        JsonObject saved = Read(referencedPath);
        Assert.Multiple(() =>
        {
            Assert.That((string?)saved["Text"], Is.EqualTo("after"));
            // Only the files of the project hierarchy carry the version fields.
            Assert.That(saved.ContainsKey("appVersion"), Is.False);
            Assert.That(saved.ContainsKey("minAppVersion"), Is.False);
        });
        AssertProjectFileRaisedFirst(replaced, referencedPath, before);
    }

    [Test]
    public void A_project_file_that_records_the_floor_is_not_rewritten()
    {
        SaveProject();
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();
        byte[] before = File.ReadAllBytes(_projectPath);

        List<Replacement> replaced = RecordReplacements(() =>
            CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Select(replacement => replacement.Path), Is.EqualTo(new[] { _scenePath }));
            Assert.That(File.ReadAllBytes(_projectPath), Is.EqualTo(before));
        });
    }

    [Test]
    public void A_project_file_recording_more_than_the_floor_is_not_lowered()
    {
        SaveProject();
        string recorded = AboveFloor;
        SetVersionFields(_projectPath, recorded, recorded);
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();

        List<Replacement> replaced = RecordReplacements(() =>
            CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(replaced.Select(replacement => replacement.Path), Is.EqualTo(new[] { _scenePath }));
            Assert.That((string?)Read(_projectPath)["minAppVersion"], Is.EqualTo(recorded));
        });
    }

    // A save writes many files. Once the project file is known to record enough, it is not read again while
    // its timestamp and length stay the same: held open without sharing, it could not be read at all.
    [Test]
    public void A_save_that_needs_no_raise_does_not_read_the_project_file_again()
    {
        SaveProject();
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();
        string sceneDirectory = Path.GetDirectoryName(_scenePath)!;
        Element[] added =
        [
            new Element { ZIndex = { Value = 1 }, Uri = new Uri(Path.Combine(sceneDirectory, "second.belm")) },
            new Element { ZIndex = { Value = 2 }, Uri = new Uri(Path.Combine(sceneDirectory, "third.belm")) },
        ];
        foreach (Element element in added)
        {
            scene.Children.Add(element);
        }

        using (new FileStream(_projectPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Writes every element file of the scene, then the scene file.
            Assert.DoesNotThrow(() => CoreSerializer.StoreToUri(scene, scene.Uri!));
        }

        Assert.That(added.Select(element => File.Exists(element.Uri!.LocalPath)), Is.All.True);
    }

    // A checkout, for example, can put an older project file in place while the project is open.
    [Test]
    public void A_project_file_changed_outside_Beutl_is_read_again()
    {
        SaveProject();
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();
        long length = new FileInfo(_projectPath).Length;
        DateTime timestamp = File.GetLastWriteTimeUtc(_projectPath);
        // The same length, and a later timestamp whatever the resolution of the file system.
        SetVersionFields(_projectPath, BeutlApplication.Version, OlderMinAppVersion);
        File.SetLastWriteTimeUtc(_projectPath, timestamp.AddSeconds(2));
        Assume.That(new FileInfo(_projectPath).Length, Is.EqualTo(length));

        CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write);

        Assert.That((string?)Read(_projectPath)["minAppVersion"], Is.EqualTo(FileFormat.Floor));
    }

    // A save must not walk past a project file it could not raise.
    [Test]
    public void A_project_file_that_cannot_be_read_stops_the_write()
    {
        SaveProject();
        SetVersionFields(_projectPath, OlderAppVersion, OlderMinAppVersion);
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();
        scene.Name.Value = "renamed";
        byte[] before = File.ReadAllBytes(_scenePath);

        using (new FileStream(_projectPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() =>
                CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write));
        }

        Assert.That(File.ReadAllBytes(_scenePath), Is.EqualTo(before));
    }

    // No build opens such a file, and the next save of the project replaces it.
    [Test]
    public void A_project_file_that_is_not_json_does_not_block_the_write()
    {
        SaveProject();
        Project project = LoadProject();
        Scene scene = (Scene)project.Items.Single();
        scene.Name.Value = "renamed";
        File.WriteAllText(_projectPath, "{ not json");

        Assert.DoesNotThrow(() => CoreSerializer.StoreToUri(scene, scene.Uri!, CoreSerializationMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(_projectPath), Is.EqualTo("{ not json"));
            Assert.That((string?)Read(_scenePath)["Name"], Is.EqualTo("renamed"));
        });
    }

    [Test]
    public void A_save_to_another_destination_does_not_raise_the_project_it_came_from()
    {
        SaveProject();
        SetVersionFields(_projectPath, OlderAppVersion, OlderMinAppVersion);
        byte[] source = File.ReadAllBytes(_projectPath);
        Project project = LoadProject();
        string destination = Path.Combine(_directory, "copy", "project.bep");
        Scene scene = (Scene)project.Items.Single();
        scene.Uri = new Uri(Path.Combine(_directory, "copy", "scene", "scene.scene"));
        Element element = scene.Children.Single();
        element.Uri = new Uri(Path.Combine(_directory, "copy", "scene", "element.belm"));

        CoreSerializer.StoreToUri(project, new Uri(destination));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(_projectPath), Is.EqualTo(source));
            Assert.That((string?)Read(destination)["minAppVersion"], Is.EqualTo(FileFormat.Floor));
        });
    }

    private void SaveProject(Element? element = null)
    {
        var project = new Project { Uri = new Uri(_projectPath) };
        var scene = new Scene { Uri = new Uri(_scenePath) };
        element ??= new Element();
        element.Uri = new Uri(_elementPath);
        scene.AddChild(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri);
    }

    private Project LoadProject() => CoreSerializer.RestoreFromUri<Project>(new Uri(_projectPath));

    private string PathOf(RootFile file) => file switch
    {
        RootFile.Project => _projectPath,
        RootFile.Scene => _scenePath,
        _ => _elementPath,
    };

    // The project file is replaced first, with the floor and this build, and only that; the file saved on
    // its own is replaced after it.
    private void AssertProjectFileRaisedFirst(List<Replacement> replaced, string saved, JsonObject before)
    {
        JsonObject after = Read(_projectPath);
        Assert.That(replaced.Select(replacement => replacement.Path), Is.EqualTo(new[] { _projectPath, saved }));
        Assert.Multiple(() =>
        {
            Assert.That(replaced[1].ProjectMinAppVersion, Is.EqualTo(FileFormat.Floor));
            Assert.That((string?)after["minAppVersion"], Is.EqualTo(FileFormat.Floor));
            Assert.That((string?)after["appVersion"], Is.EqualTo(BeutlApplication.Version));
            Assert.That(after.Select(pair => pair.Key), Is.EqualTo(before.Select(pair => pair.Key)));
            Assert.That(JsonNode.DeepEquals(WithoutVersionFields(after), WithoutVersionFields(before)), Is.True);
        });
    }

    // Runs action and returns the files it replaced, in order, each with the minAppVersion the project
    // file recorded just before the replacement.
    private List<Replacement> RecordReplacements(Action action)
    {
        var replaced = new List<Replacement>();
        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace)
                   {
                       lock (replaced)
                       {
                           replaced.Add(new Replacement(path, (string?)Read(_projectPath)["minAppVersion"]));
                       }
                   }
               }))
        {
            action();
        }

        return replaced;
    }

    private static UnsupportedFileVersionException CatchRefusal(Action read)
    {
        Exception? thrown = Assert.Catch(() => read());
        UnsupportedFileVersionException? refused = thrown is null
            ? null
            : ExceptionHelpers.Find<UnsupportedFileVersionException>(thrown);
        Assert.That(refused, Is.Not.Null, thrown?.ToString());
        return refused!;
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void Edit(string path, Action<JsonObject> edit)
    {
        JsonObject json = Read(path);
        edit(json);
        json.JsonSave(path);
    }

    private static void SetVersionFields(string path, string? appVersion, string? minAppVersion)
    {
        Edit(path, json =>
        {
            if (appVersion is null) json.Remove("appVersion");
            else json["appVersion"] = appVersion;
            if (minAppVersion is null) json.Remove("minAppVersion");
            else json["minAppVersion"] = minAppVersion;
        });
    }

    private static JsonObject WithoutVersionFields(JsonObject json)
    {
        var copy = (JsonObject)json.DeepClone();
        copy.Remove("appVersion");
        copy.Remove("minAppVersion");
        return copy;
    }

    private sealed record Replacement(string Path, string? ProjectMinAppVersion);

    private sealed class ReferencingElement : Element
    {
        public ReferencedFile? Reference { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Reference), Reference);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Reference = context.GetValue<ReferencedFile>(nameof(Reference));
        }
    }

    // A file-backed object reached through an ordinary property rather than the hierarchy.
    private sealed class ReferencedFile : CoreObject
    {
        public string? Text { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Text), Text);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Text = context.GetValue<string>(nameof(Text));
        }
    }
}
