using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

// Scale machinery is runtime-only: existing projects load with zero migration. Non-GPU.
[TestFixture]
public class NoMigrationRegressionTests
{
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"beutl-no-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Test]
    public void Dilate_DoesNotSerializeScaleMachinery_AndRoundTripsStably()
    {
        var dilate = new Dilate();
        dilate.RadiusX.CurrentValue = 5f;
        dilate.RadiusY.CurrentValue = 7f;

        var json = CoreSerializer.SerializeToJsonObject(dilate);
        string s1 = json.ToJsonString();

        // Scale machinery has no serialized CoreProperty, so none of it leaks into the JSON.
        Assert.That(s1, Does.Not.Contain("WorkingScale"));
        Assert.That(s1, Does.Not.Contain("EffectiveScale"));
        Assert.That(s1, Does.Not.Contain("OutputScale"));

        var restored = (Dilate)CoreSerializer.DeserializeFromJsonObject(json, typeof(Dilate));
        string s2 = CoreSerializer.SerializeToJsonObject(restored).ToJsonString();
        Assert.That(s2, Is.EqualTo(s1), "round-trip must be byte-stable (no migration / format drift)");
    }

    [Test]
    public void BlurredEllipse_RoundTripsStably_AndIsCurrentFormat()
    {
        var shape = new EllipseShape();
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 80;
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(4, 4);
        shape.FilterEffect.CurrentValue = blur;

        var json = CoreSerializer.SerializeToJsonObject(shape);
        string s1 = json.ToJsonString();

        // Current EngineObject format, not the effect-item Operation/Children one that ElementMigration handles.
        Assert.That(s1, Does.Not.Contain("\"Operation\""));
        Assert.That(s1, Does.Not.Contain("WorkingScale"));

        var restored = (EllipseShape)CoreSerializer.DeserializeFromJsonObject(json, typeof(EllipseShape));
        string s2 = CoreSerializer.SerializeToJsonObject(restored).ToJsonString();
        Assert.That(s2, Is.EqualTo(s1), "round-trip must be byte-stable");
    }

    [Test]
    public void Project_plain_resave_preserves_loaded_app_version_byte_for_byte()
    {
        string path = Path.Combine(_tempDirectory, "project.bep");
        var source = new Project
        {
            Uri = new Uri(path),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["appVersion"] = "3.1.4";
        json["minAppVersion"] = Project.DefaultMinAppVersion;
        json.JsonSave(path);
        byte[] before = File.ReadAllBytes(path);

        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(path));
        CoreSerializer.StoreToUri(restored, new Uri(path));
        byte[] after = File.ReadAllBytes(path);

        Assert.Multiple(() =>
        {
            Assert.That(restored.AppVersion, Is.EqualTo("3.1.4"));
            Assert.That(after, Is.EqualTo(before));
        });
    }

    [Test]
    public void JsonSave_always_writes_lf_line_endings()
    {
        string path = Path.Combine(_tempDirectory, "line-endings.json");
        var json = new JsonObject
        {
            ["name"] = "Beutl",
            ["items"] = new JsonArray(1, 2, 3),
        };

        json.JsonSave(path);
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.UTF8.GetString(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("\n"));
            Assert.That(text, Does.Not.Contain("\r\n"));
            Assert.That(bytes, Has.None.EqualTo((byte)'\r'));
        });
    }

    [Test]
    public void Project_marked_as_migrated_advances_app_version()
    {
        string path = Path.Combine(_tempDirectory, "project.bep");
        var source = new Project
        {
            Uri = new Uri(path),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["appVersion"] = "3.1.4";
        json.JsonSave(path);
        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(path));

        restored.MarkAsMigrated();
        JsonObject migrated = CoreSerializer.SerializeToJsonObject(restored);

        Assert.That((string?)migrated["appVersion"], Is.EqualTo(BeutlApplication.Version));
    }

    [Test]
    public void Project_aggregates_migration_versions_from_items_attached_after_loading()
    {
        string path = Path.Combine(_tempDirectory, "project.bep");
        var source = new Project { Uri = new Uri(path) };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["appVersion"] = "3.1.4";
        json["minAppVersion"] = Project.DefaultMinAppVersion;
        json.JsonSave(path);
        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(path));

        restored.Items.Add(CreateMigrated(new MigratedProjectItem("4.2.0")));
        restored.Items.Add(CreateMigrated(new MigratedProjectItem("5.1.0")));

        Assert.Multiple(() =>
        {
            Assert.That(restored.AppVersion, Is.EqualTo(BeutlApplication.Version));
            Assert.That(restored.MinAppVersion, Is.EqualTo("5.1.0"));
        });
    }

    [Test]
    public void Project_serialization_aggregates_plugin_element_migrations()
    {
        var project = new Project
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "project.bep")),
        };
        var scene = new Scene
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "scene.scene")),
        };
        project.Items.Add(scene);
        scene.AddChild(CreateMigrated(new MigratedElement("6.0.0")
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "element.belm")),
        }));

        JsonObject json = CoreSerializer.SerializeToJsonObject(project);

        Assert.Multiple(() =>
        {
            Assert.That(project.MinAppVersion, Is.EqualTo("6.0.0"));
            Assert.That((string?)json["minAppVersion"], Is.EqualTo("6.0.0"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Deserialization_aggregates_non_hierarchical_serialized_child_migrations(
        bool deserializeFromNode)
    {
        var source = new MigratingContainer
        {
            First = new MigratingLeaf("7.0.0"),
            Second = new MigratingLeaf("8.0.0"),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);

        var restored = (MigratingContainer)(deserializeFromNode
            ? CoreSerializer.DeserializeFromJsonNode(json, typeof(ProjectItem))!
            : CoreSerializer.DeserializeFromJsonObject(json, typeof(ProjectItem)));
        var project = new Project();
        project.Items.Add(restored);

        Assert.That(project.MinAppVersion, Is.EqualTo("8.0.0"));
    }

    [Test]
    public void Invalid_migration_version_is_rejected()
    {
        var context = new JsonSerializationContext(
            typeof(MigratingLeaf),
            options: new CoreSerializerOptions { Mode = CoreSerializationMode.Read });

        Assert.Throws<InvalidOperationException>(() =>
            context.ReportPersistedContentMigration("not-a-version"));
    }

    [Test]
    public void Resolve_callback_migration_is_propagated_after_deserialization()
    {
        var owner = new MigratingContainer();
        var target = new Scene();
        var rootContext = new JsonSerializationContext(
            typeof(MigratingContainer),
            options: new CoreSerializerOptions { Mode = CoreSerializationMode.Read });
        var targetContext = new JsonSerializationContext(
            typeof(Scene),
            rootContext,
            options: new CoreSerializerOptions { Mode = CoreSerializationMode.Read });
        targetContext.AfterDeserialized(target);
        rootContext.Resolve(
            target.Id,
            _ => rootContext.ReportPersistedContentMigration("9.0.0"));

        rootContext.AfterDeserialized(owner);
        var project = new Project();
        project.Items.Add(owner);

        Assert.That(project.MinAppVersion, Is.EqualTo("9.0.0"));
    }

    [Test]
    public void Concurrent_context_reports_keep_the_highest_migration_version()
    {
        var owner = new MigratingContainer();
        var firstContext = new JsonSerializationContext(typeof(MigratingContainer));
        var secondContext = new JsonSerializationContext(typeof(MigratingContainer));
        firstContext.AfterDeserialized(owner);
        secondContext.AfterDeserialized(owner);

        Parallel.Invoke(
            () => firstContext.ReportPersistedContentMigration("9.0.0"),
            () => secondContext.ReportPersistedContentMigration("7.0.0"));
        var project = new Project();
        project.Items.Add(owner);

        Assert.That(project.MinAppVersion, Is.EqualTo("9.0.0"));
    }

    [Test]
    public void PopulateFromJsonObject_aggregates_serialized_child_migrations()
    {
        var source = new MigratingContainer
        {
            First = new MigratingLeaf("7.0.0"),
            Second = new MigratingLeaf("8.0.0"),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = new MigratingContainer();

        CoreSerializer.PopulateFromJsonObject(restored, json);
        var project = new Project();
        project.Items.Add(restored);

        Assert.That(project.MinAppVersion, Is.EqualTo("8.0.0"));
    }

    [Test]
    public void A_standalone_value_attached_afterwards_migrates_the_project_it_is_saved_into()
    {
        MigratingLeaf leaf = CreateMigrated(new MigratingLeaf("7.0.0"));
        var owner = new MigratingContainer();
        string path = Path.Combine(_tempDirectory, "project.bep");
        var project = new Project { Uri = new Uri(path) };
        project.Items.Add(owner);
        // The value reported to a context that has already ended, so no owner knows about it yet.
        Assert.That(project.MinAppVersion, Is.EqualTo(Project.DefaultMinAppVersion));

        owner.First = leaf;
        CoreSerializer.StoreToUri(project, project.Uri);

        JsonObject saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(project.MinAppVersion, Is.EqualTo("7.0.0"));
            Assert.That((string?)saved["minAppVersion"], Is.EqualTo("7.0.0"));
        });
    }

    [Test]
    public void Standalone_values_attached_to_one_owner_keep_the_highest_migration()
    {
        var owner = new MigratingContainer
        {
            First = CreateMigrated(new MigratingLeaf("7.0.0")),
            Second = CreateMigrated(new MigratingLeaf("9.0.0")),
        };
        string path = Path.Combine(_tempDirectory, "project.bep");
        var project = new Project { Uri = new Uri(path) };
        project.Items.Add(owner);

        CoreSerializer.StoreToUri(project, project.Uri);

        JsonObject saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That((string?)saved["minAppVersion"], Is.EqualTo("9.0.0"));
    }

    [Test]
    public void Saving_a_standalone_value_hands_its_migration_to_the_owner_for_later_saves()
    {
        var owner = new MigratingContainer { First = CreateMigrated(new MigratingLeaf("7.0.0")) };
        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.Null);

        CoreSerializer.SerializeToJsonObject(owner);

        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.EqualTo("7.0.0"));
    }

    // A converter reaches CoreSerializer's root entry point rather than SerializeCoreSerializable,
    // so the requirement has to be handed over there too.
    [Test]
    public void A_standalone_value_reached_through_a_converter_migrates_its_owner()
    {
        var owner = new OptionalValueOwner
        {
            Value = new Optional<MigratingLeaf>(CreateMigrated(new MigratingLeaf("7.0.0"))),
        };
        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.Null);

        CoreSerializer.SerializeToJsonObject(owner);

        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.EqualTo("7.0.0"));
    }

    // The metadata is written before the items and rewritten afterwards, so it must keep the place
    // the released format gives it rather than move to the end of the file.
    [TestCase(false)]
    [TestCase(true)]
    public void Project_keys_keep_the_released_order_whether_or_not_a_migration_arrives_late(
        bool migrate)
    {
        string path = Path.Combine(_tempDirectory, "project.bep");
        var project = new Project { Uri = new Uri(path) };
        project.Items.Add(migrate
            ? new MigratingContainer { First = CreateMigrated(new MigratingLeaf("7.0.0")) }
            : new MigratingContainer());

        CoreSerializer.StoreToUri(project, project.Uri);

        Assert.That(
            string.Join(",", JsonNode.Parse(File.ReadAllText(path))!.AsObject().Select(property => property.Key)),
            Is.EqualTo("Id,Name,appVersion,minAppVersion,items,variables,$type"));
    }

    [Test]
    public void A_failed_standalone_deserialization_retains_no_migration()
    {
        var leaf = new ThrowingMigrationLeaf();
        JsonObject json = CoreSerializer.SerializeToJsonObject(leaf);
        Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.PopulateFromJsonObject(leaf, json));

        var owner = new StandaloneValueOwner { Value = leaf };
        CoreSerializer.SerializeToJsonObject(owner);

        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.Null);
    }

    [Test]
    public void A_retained_standalone_migration_does_not_keep_its_value_alive()
    {
        WeakReference reference = CreateMigratedLeafReference();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(reference.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateMigratedLeafReference()
    {
        MigratingLeaf leaf = CreateMigrated(new MigratingLeaf("7.0.0"));
        Assert.That(AttachedContentMigrations.Get(leaf), Is.EqualTo("7.0.0"));
        return new WeakReference(leaf);
    }

    [Test]
    public void Failed_deserialization_does_not_attach_a_reported_migration()
    {
        var item = new ThrowingMigrationItem();
        JsonObject json = CoreSerializer.SerializeToJsonObject(item);

        Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.PopulateFromJsonObject(item, json));
        var project = new Project();
        project.Items.Add(item);

        Assert.That(project.MinAppVersion, Is.EqualTo(Project.DefaultMinAppVersion));
    }

    [Test]
    public void Failed_legacy_population_does_not_mark_the_retained_object_as_migrated()
    {
        string path = Path.Combine(_tempDirectory, "legacy-failure.scene");
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            new ThrowingLegacyProjectItem());
        json.Remove("$type");
        json.JsonSave(path);
        var retained = new ThrowingLegacyProjectItem();
        var project = new Project();
        project.RestoreVersionMetadata("3.1.4", "1.0.0");
        project.Items.Add(retained);

        Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.PopulateFromUri(
                retained,
                typeof(ProjectItem),
                new Uri(path)));
        JsonObject serialized = CoreSerializer.SerializeToJsonObject(project);

        Assert.Multiple(() =>
        {
            Assert.That(project.AppVersion, Is.EqualTo("3.1.4"));
            Assert.That(project.MinAppVersion, Is.EqualTo("1.0.0"));
            Assert.That((string?)serialized["appVersion"], Is.EqualTo("3.1.4"));
            Assert.That((string?)serialized["minAppVersion"], Is.EqualTo("1.0.0"));
        });
    }

    [Test]
    public void Serialization_graph_accepts_temporary_migration_reports()
    {
        Assert.DoesNotThrow(() =>
            VersionControlSerializationGraph.DiscoverSerializationGraph(
                new MigrationNodeContainer()));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Json_population_records_missing_discriminators(bool populateElement)
    {
        var scene = new Scene();
        var element = new Element();
        scene.Children.Add(element);
        var project = new Project();
        project.RestoreVersionMetadata("1.0.0", "1.0.0");
        project.Items.Add(scene);
        CoreObject target = populateElement ? element : scene;
        var options = new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(target, options);
        json.Remove("$type");

        CoreSerializer.PopulateFromJsonObject(target, json, options);
        CoreSerializer.SerializeToJsonObject(project, options);

        Assert.That(project.MinAppVersion, Is.EqualTo(Project.DefaultMinAppVersion));
    }

    [Test]
    public void Detached_sidecar_migration_reaches_its_deserializing_owner()
    {
        string path = Path.Combine(_tempDirectory, "detached.belm");
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Element());
        json.Remove("$type");
        json.JsonSave(path);
        var owner = new DetachedSidecarOwner { SidecarPath = path };
        JsonObject ownerJson = CoreSerializer.SerializeToJsonObject(owner);
        var restored = (DetachedSidecarOwner)CoreSerializer.DeserializeFromJsonObject(
            ownerJson, typeof(DetachedSidecarOwner));
        var project = new Project();
        project.RestoreVersionMetadata("1.0.0", "1.0.0");
        project.Items.Add(restored);

        Assert.That(restored.Child!.HierarchicalParent, Is.Null);
        Assert.That(project.MinAppVersion, Is.EqualTo(Project.DefaultMinAppVersion));
    }

    private sealed class DetachedSidecarOwner : ProjectItem
    {
        public string SidecarPath { get; set; } = null!;
        public Element? Child { get; private set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(SidecarPath), SidecarPath);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            SidecarPath = context.GetValue<string>(nameof(SidecarPath))!;
            Child = CoreSerializer.RestoreFromUri<Element>(new Uri(SidecarPath));
        }
    }

    [Test]
    public void PopulateFromUri_reports_a_legacy_discriminator_migration()
    {
        string scenePath = Path.Combine(_tempDirectory, "legacy.scene");
        var source = new Scene { Uri = new Uri(scenePath) };
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        json.Remove("$type");
        json.JsonSave(scenePath);
        JsonObject projectJson = CoreSerializer.SerializeToJsonObject(new Project());
        projectJson["appVersion"] = "1.0.0";
        projectJson["minAppVersion"] = "1.0.0";
        var project = (Project)CoreSerializer.DeserializeFromJsonObject(
            projectJson,
            typeof(Project));
        ProjectItem scene = new Scene { Uri = new Uri(scenePath) };
        project.Items.Add(scene);

        CoreSerializer.PopulateFromUri(scene, new Uri(scenePath));
        CoreSerializer.SerializeToJsonObject(project);

        Assert.That(project.AppVersion, Is.EqualTo(BeutlApplication.Version));
        Assert.That(project.MinAppVersion, Is.EqualTo(Project.DefaultMinAppVersion));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Project_with_legacy_sidecar_discriminator_advances_app_version(
        bool removeSceneDiscriminator,
        bool removeElementDiscriminator)
    {
        (string projectPath, string scenePath, string elementPath) = CreateProjectWithSidecars();
        SetPersistedAppVersion(projectPath, "1.0.0");
        if (removeSceneDiscriminator)
        {
            JsonObject sceneJson = JsonNode.Parse(File.ReadAllText(scenePath))!.AsObject();
            sceneJson.Remove("$type");
            sceneJson.JsonSave(scenePath);
        }

        if (removeElementDiscriminator)
        {
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            elementJson.Remove("$type");
            elementJson.JsonSave(elementPath);
        }

        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));

        Assert.That(restored.AppVersion, Is.EqualTo(BeutlApplication.Version));
    }

    [Test]
    public void Project_with_current_sidecars_preserves_loaded_app_version()
    {
        (string projectPath, _, _) = CreateProjectWithSidecars();
        SetPersistedAppVersion(projectPath, "3.1.4");

        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        CoreSerializer.StoreToUri(restored, new Uri(projectPath));
        JsonObject resaved = JsonNode.Parse(File.ReadAllText(projectPath))!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(restored.AppVersion, Is.EqualTo("3.1.4"));
            Assert.That((string?)resaved["appVersion"], Is.EqualTo("3.1.4"));
        });
    }

    [TestCase(typeof(ProjectItem))]
    [TestCase(typeof(Element))]
    public void Present_unresolvable_sidecar_discriminator_is_not_treated_as_legacy(Type type)
    {
        const string unknownDiscriminator = "[Missing.Extension]Missing.Extension:UnknownItem";
        string path = Path.Combine(_tempDirectory, "unknown-sidecar.json");
        var json = new JsonObject
        {
            ["$type"] = unknownDiscriminator,
        };
        json.JsonSave(path);

        Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.RestoreFromUri(new Uri(path), type));

        JsonObject preserved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.That((string?)preserved["$type"], Is.EqualTo(unknownDiscriminator));
    }

    [Test]
    public void Project_with_empty_legacy_operation_advances_app_version()
    {
        (string projectPath, _, string elementPath) = CreateProjectWithSidecars();
        SetPersistedAppVersion(projectPath, "1.0.0");
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        elementJson.Remove(nameof(Element.Objects));
        elementJson["Operation"] = new JsonObject
        {
            ["Children"] = new JsonArray(),
        };
        elementJson.JsonSave(elementPath);

        Project restored = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));

        Assert.Multiple(() =>
        {
            Assert.That(restored.AppVersion, Is.EqualTo(BeutlApplication.Version));
            Assert.That(((Scene)restored.Items.Single()).Children.Single().Objects, Is.Empty);
        });
    }

    [Test]
    public void AutoSave_persists_migration_requirements_before_writing_an_element()
    {
        var project = new Project { Uri = new Uri(Path.Combine(_tempDirectory, "project.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "scene.scene")) };
        var element = CreateMigrated(new MigratedElement("9.0.0"));
        element.Uri = new Uri(Path.Combine(_tempDirectory, "element.belm"));
        scene.Children.Add(element);
        project.Items.Add(scene);
        var root = new VirtualProjectRoot();
        root.AttachProject(project);
        File.WriteAllText(project.Uri.LocalPath, "{\"minAppVersion\":\"1.0.0\"}");
        File.WriteAllText(element.Uri.LocalPath, "original");
        string? requiredVersionAtElementWrite = null;
        element.BeforeSerialization = () => requiredVersionAtElementWrite =
            (string?)JsonNode.Parse(File.ReadAllText(project.Uri.LocalPath))!["minAppVersion"];
        using var autoSave = new AutoSaveService();

        autoSave.SaveObjects([element]);

        Assert.That(requiredVersionAtElementWrite, Is.EqualTo("9.0.0"));
        Assert.That(File.ReadAllText(element.Uri.LocalPath), Is.Not.EqualTo("original"));
    }

    [Test]
    public void A_project_save_writes_the_gate_before_the_referenced_files_it_guards()
    {
        string projectPath = Path.Combine(_tempDirectory, "project.bep");
        var project = new Project { Uri = new Uri(projectPath) };
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "scene.scene")) };
        var element = new StandaloneValueElement
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "element.belm")),
            Value = CreateMigrated(new MigratingLeaf("9.0.0")),
        };
        scene.Children.Add(element);
        project.Items.Add(scene);
        File.WriteAllText(projectPath, "{\"minAppVersion\":\"1.0.0\"}");
        string? gateAtElementWrite = null;
        element.BeforeSerialization = () => gateAtElementWrite =
            (string?)JsonNode.Parse(File.ReadAllText(projectPath))!["minAppVersion"];

        // A project writes the files it references before its own bytes reach the disk, so the
        // requirement has to be persisted by a preflight rather than by this save's own metadata.
        CoreSerializer.StoreToUri(project, project.Uri);

        Assert.That(gateAtElementWrite, Is.EqualTo("9.0.0"));
    }

    [Test]
    public void A_project_that_already_covers_a_standalone_migration_skips_the_discovery_pass()
    {
        int serializations = 0;
        (Project project, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("project.bep", CreateMigrated(new MigratingLeaf("9.0.0")));
        element.BeforeSerialization = () => serializations++;

        CoreSerializer.StoreToUri(project, project.Uri!);
        int discovering = serializations;
        serializations = 0;
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(discovering, Is.GreaterThan(1), "the first save has to discover the requirement");
            Assert.That(serializations, Is.EqualTo(1), "the gate already covers it, so nothing is discovered");
        });
    }

    // The destination is gated before the files that gate guards, but the preflight records no item
    // graph: a save that fails after it must not leave a project pointing at sidecars it never wrote.
    [TestCase(true)]
    [TestCase(false)]
    public void A_project_saved_to_a_new_destination_is_gated_without_an_item_graph(bool firstSave)
    {
        (Project project, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("project.bep", CreateMigrated(new MigratingLeaf("9.0.0")));
        string sourcePath = project.Uri!.LocalPath;
        File.WriteAllText(sourcePath, "{\"minAppVersion\":\"1.0.0\"}");
        if (firstSave)
        {
            project.Uri = null;
        }

        string destinationPath = Path.Combine(_tempDirectory, "elsewhere", "project.bep");
        JsonObject? destinationAtElementWrite = null;
        element.BeforeSerialization = () => destinationAtElementWrite ??= File.Exists(destinationPath)
            ? JsonNode.Parse(File.ReadAllText(destinationPath))!.AsObject()
            : null;

        CoreSerializer.StoreToUri(project, new Uri(destinationPath));

        Assert.Multiple(() =>
        {
            Assert.That((string?)destinationAtElementWrite?["minAppVersion"], Is.EqualTo("9.0.0"));
            Assert.That(destinationAtElementWrite?.ContainsKey("items"), Is.False);
            Assert.That(
                (string?)JsonNode.Parse(File.ReadAllText(destinationPath))!["minAppVersion"],
                Is.EqualTo("9.0.0"));
            if (!firstSave)
            {
                Assert.That(
                    (string?)JsonNode.Parse(File.ReadAllText(sourcePath))!["minAppVersion"],
                    Is.EqualTo("1.0.0"),
                    "a Save As must not rewrite the project it came from");
            }
        });
    }

    [Test]
    public void A_standalone_value_type_kept_as_an_interface_box_migrates_its_owner()
    {
        var owner = new StandaloneValueOwner
        {
            Value = (ICoreSerializable)CoreSerializer.DeserializeFromJsonObject(
                CoreSerializer.SerializeToJsonObject(new MigratingStructLeaf("7.0.0")),
                typeof(MigratingStructLeaf)),
        };
        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.Null);

        CoreSerializer.SerializeToJsonObject(owner);

        // The box the deserializer returned is the instance the owner holds, so it stays trackable.
        Assert.That(Project.GetRequiredMigrationVersion(owner), Is.EqualTo("7.0.0"));
    }

    [Test]
    public void The_migration_preflight_leaves_the_persisted_item_graph_alone()
    {
        (Project project, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("project.bep", CreateMigrated(new MigratingLeaf("9.0.0")));
        string path = project.Uri!.LocalPath;
        File.WriteAllText(path, "{\"minAppVersion\":\"1.0.0\",\"items\":[\"already-there.scene\"]}");
        JsonObject? gateAtElementWrite = null;
        element.BeforeSerialization = () =>
            gateAtElementWrite = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        CoreSerializer.StoreToUri(project, project.Uri);

        Assert.Multiple(() =>
        {
            Assert.That((string?)gateAtElementWrite?["minAppVersion"], Is.EqualTo("9.0.0"));
            // The save that follows can still fail, and ProjectPersistence then rolls the in-memory
            // item list back; the preflight must not have recorded a graph that never happened.
            Assert.That(
                gateAtElementWrite?["items"]?.ToJsonString(),
                Is.EqualTo("[\"already-there.scene\"]"));
        });
    }

    [Test]
    public void The_migration_preflight_does_not_lower_a_gate_already_on_disk()
    {
        (Project project, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("project.bep", CreateMigrated(new MigratingLeaf("9.0.0")));
        string path = project.Uri!.LocalPath;
        File.WriteAllText(path, "{\"minAppVersion\":\"99.0.0\"}");
        string? gateAtElementWrite = null;
        element.BeforeSerialization = () => gateAtElementWrite =
            (string?)JsonNode.Parse(File.ReadAllText(path))!["minAppVersion"];

        CoreSerializer.StoreToUri(project, project.Uri);

        Assert.That(gateAtElementWrite, Is.EqualTo("99.0.0"));
    }

    [Test]
    public void A_malformed_project_file_does_not_block_the_save_that_replaces_it()
    {
        (Project project, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("project.bep", CreateMigrated(new MigratingLeaf("9.0.0")));
        File.WriteAllText(project.Uri!.LocalPath, "{ this is not json");

        Assert.DoesNotThrow(() => CoreSerializer.StoreToUri(project, project.Uri));

        Assert.Multiple(() =>
        {
            Assert.That(
                (string?)JsonNode.Parse(File.ReadAllText(project.Uri.LocalPath))!["minAppVersion"],
                Is.EqualTo("9.0.0"));
            Assert.That(File.Exists(element.Uri!.LocalPath), Is.True);
        });
    }

    [Test]
    public void An_unreadable_persisted_gate_does_not_fail_the_save()
    {
        (Project project, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("project.bep", CreateMigrated(new MigratingLeaf("9.0.0")));
        project.RestoreVersionMetadata("not-a-version", "not-a-version");
        File.WriteAllText(project.Uri!.LocalPath, "{\"minAppVersion\":\"not-a-version\"}");

        Assert.DoesNotThrow(() => CoreSerializer.StoreToUri(project, project.Uri));
        Assert.That(File.Exists(element.Uri!.LocalPath), Is.True);
    }

    [Test]
    public void A_standalone_value_shared_with_another_project_is_discovered_again()
    {
        MigratingLeaf leaf = CreateMigrated(new MigratingLeaf("9.0.0"));
        (Project first, StandaloneValueElement _) = CreateProjectWithStandaloneValue("first.bep", leaf);
        CoreSerializer.StoreToUri(first, first.Uri!);

        // The same value now reaches a second project, whose gate knows nothing about it.
        (Project second, StandaloneValueElement element) =
            CreateProjectWithStandaloneValue("second.bep", leaf);
        File.WriteAllText(second.Uri!.LocalPath, "{\"minAppVersion\":\"1.0.0\"}");
        string? gateAtElementWrite = null;
        element.BeforeSerialization = () => gateAtElementWrite =
            (string?)JsonNode.Parse(File.ReadAllText(second.Uri.LocalPath))!["minAppVersion"];

        CoreSerializer.StoreToUri(second, second.Uri);

        Assert.That(gateAtElementWrite, Is.EqualTo("9.0.0"));
    }

    private (Project Project, StandaloneValueElement Element) CreateProjectWithStandaloneValue(
        string projectFileName,
        MigratingLeaf leaf)
    {
        string directory = Path.Combine(_tempDirectory, Path.GetFileNameWithoutExtension(projectFileName));
        Directory.CreateDirectory(directory);
        var project = new Project { Uri = new Uri(Path.Combine(directory, projectFileName)) };
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "scene.scene")) };
        var element = new StandaloneValueElement
        {
            Uri = new Uri(Path.Combine(directory, "element.belm")),
            Value = leaf,
        };
        scene.Children.Add(element);
        project.Items.Add(scene);
        return (project, element);
    }

    [Test]
    public void AutoSave_persists_a_standalone_value_migration_before_writing_the_element()
    {
        var project = new Project { Uri = new Uri(Path.Combine(_tempDirectory, "project.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "scene.scene")) };
        var element = new StandaloneValueElement
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "element.belm")),
            Value = CreateMigrated(new MigratingLeaf("9.0.0")),
        };
        scene.Children.Add(element);
        project.Items.Add(scene);
        var root = new VirtualProjectRoot();
        root.AttachProject(project);
        File.WriteAllText(project.Uri.LocalPath, "{\"minAppVersion\":\"1.0.0\"}");
        File.WriteAllText(element.Uri.LocalPath, "original");
        string? requiredVersionAtElementWrite = null;
        element.BeforeSerialization = () => requiredVersionAtElementWrite =
            (string?)JsonNode.Parse(File.ReadAllText(project.Uri.LocalPath))!["minAppVersion"];
        using var autoSave = new AutoSaveService();

        autoSave.SaveObjects([element]);

        Assert.Multiple(() =>
        {
            // The gate has to be on disk before the sidecar carrying the migrated value replaces it.
            Assert.That(requiredVersionAtElementWrite, Is.EqualTo("9.0.0"));
            Assert.That(File.ReadAllText(element.Uri.LocalPath), Is.Not.EqualTo("original"));
        });
    }

    [Test]
    public void AutoSave_does_not_replace_sidecars_when_migration_preflight_fails()
    {
        string blockedPath = Path.Combine(_tempDirectory, "project.bep");
        Directory.CreateDirectory(blockedPath);
        var project = new Project { Uri = new Uri(blockedPath) };
        var scene = new Scene { Uri = new Uri(Path.Combine(_tempDirectory, "scene.scene")) };
        var element = CreateMigrated(new MigratedElement("9.0.0"));
        element.Uri = new Uri(Path.Combine(_tempDirectory, "element.belm"));
        scene.Children.Add(element);
        project.Items.Add(scene);
        var root = new VirtualProjectRoot();
        root.AttachProject(project);
        File.WriteAllText(scene.Uri.LocalPath, "original scene");
        File.WriteAllText(element.Uri.LocalPath, "original element");
        using var autoSave = new AutoSaveService();
        var errors = new List<Exception>();
        using var subscription = autoSave.SaveError.Subscribe(errors.Add);

        autoSave.SaveObjects([element, scene, project]);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(scene.Uri.LocalPath), Is.EqualTo("original scene"));
        Assert.That(File.ReadAllText(element.Uri.LocalPath), Is.EqualTo("original element"));
    }

    private (string ProjectPath, string ScenePath, string ElementPath) CreateProjectWithSidecars()
    {
        string projectPath = Path.Combine(_tempDirectory, "project.bep");
        string scenePath = Path.Combine(_tempDirectory, "scene", "scene.scene");
        string elementPath = Path.Combine(_tempDirectory, "scene", "element.belm");
        var source = new Project { Uri = new Uri(projectPath) };
        var scene = new Scene { Uri = new Uri(scenePath) };
        scene.AddChild(new Element { Uri = new Uri(elementPath) });
        source.Items.Add(scene);
        CoreSerializer.StoreToUri(
            source,
            source.Uri,
            CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);
        return (projectPath, scenePath, elementPath);
    }

    private static void SetPersistedAppVersion(string projectPath, string version)
    {
        JsonObject projectJson = JsonNode.Parse(File.ReadAllText(projectPath))!.AsObject();
        projectJson["appVersion"] = version;
        projectJson.JsonSave(projectPath);
    }

    private sealed class MigratedProjectItem : ProjectItem
    {
        public MigratedProjectItem()
        {
        }

        public MigratedProjectItem(string requiredVersion)
        {
            RequiredVersion = requiredVersion;
        }

        public string RequiredVersion { get; set; } = null!;

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(RequiredVersion), RequiredVersion);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            RequiredVersion = context.GetValue<string>(nameof(RequiredVersion))!;
            context.ReportPersistedContentMigration(RequiredVersion);
        }
    }

    private sealed class MigratedElement : Element
    {
        public MigratedElement()
        {
        }

        public MigratedElement(string requiredVersion)
        {
            RequiredVersion = requiredVersion;
        }

        public string RequiredVersion { get; set; } = null!;

        public Action? BeforeSerialization { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            BeforeSerialization?.Invoke();
            base.Serialize(context);
            context.SetValue(nameof(RequiredVersion), RequiredVersion);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            RequiredVersion = context.GetValue<string>(nameof(RequiredVersion))!;
            context.ReportPersistedContentMigration(RequiredVersion);
        }
    }

    private sealed class MigratingContainer : ProjectItem
    {
        public MigratingLeaf? First { get; set; }

        public MigratingLeaf? Second { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(First), First);
            context.SetValue(nameof(Second), Second);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            First = context.GetValue<MigratingLeaf>(nameof(First));
            Second = context.GetValue<MigratingLeaf>(nameof(Second));
        }
    }

    private sealed class MigratingLeaf : ICoreSerializable
    {
        public MigratingLeaf()
        {
        }

        public MigratingLeaf(string requiredVersion)
        {
            RequiredVersion = requiredVersion;
        }

        public string RequiredVersion { get; set; } = null!;

        public void Serialize(ICoreSerializationContext context)
        {
            context.SetValue(nameof(RequiredVersion), RequiredVersion);
        }

        public void Deserialize(ICoreSerializationContext context)
        {
            RequiredVersion = context.GetValue<string>(nameof(RequiredVersion))!;
            context.ReportPersistedContentMigration(RequiredVersion);
        }
    }

    private sealed class OptionalValueOwner : ProjectItem
    {
        public Optional<MigratingLeaf> Value { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Value), Value);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Value = context.GetValue<Optional<MigratingLeaf>>(nameof(Value));
        }
    }

    private sealed class StandaloneValueElement : Element
    {
        public MigratingLeaf? Value { get; set; }

        public Action? BeforeSerialization { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            BeforeSerialization?.Invoke();
            base.Serialize(context);
            context.SetValue(nameof(Value), Value);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Value = context.GetValue<MigratingLeaf>(nameof(Value));
        }
    }

    private sealed class StandaloneValueOwner : ProjectItem
    {
        public ICoreSerializable? Value { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Value), Value);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Value = context.GetValue<ICoreSerializable>(nameof(Value));
        }
    }

    private struct MigratingStructLeaf : ICoreSerializable
    {
        public MigratingStructLeaf()
        {
        }

        public MigratingStructLeaf(string requiredVersion)
        {
            RequiredVersion = requiredVersion;
        }

        public string RequiredVersion { get; set; } = null!;

        public void Serialize(ICoreSerializationContext context)
        {
            context.SetValue(nameof(RequiredVersion), RequiredVersion);
        }

        public void Deserialize(ICoreSerializationContext context)
        {
            RequiredVersion = context.GetValue<string>(nameof(RequiredVersion))!;
            context.ReportPersistedContentMigration(RequiredVersion);
        }
    }

    private sealed class ThrowingMigrationLeaf : ICoreSerializable
    {
        public void Serialize(ICoreSerializationContext context)
        {
            context.SetValue("RequiredVersion", "9.0.0");
        }

        public void Deserialize(ICoreSerializationContext context)
        {
            context.ReportPersistedContentMigration("9.0.0");
            throw new InvalidOperationException("migration failed");
        }
    }

    private sealed class ThrowingMigrationItem : ProjectItem
    {
        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            context.ReportPersistedContentMigration("9.0.0");
            throw new InvalidOperationException("migration failed");
        }
    }

    private sealed class ThrowingLegacyProjectItem : ProjectItem
    {
        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            throw new InvalidOperationException("legacy population failed");
        }
    }

    private sealed class MigrationNodeContainer : ProjectItem
    {
        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            var jsonContext = (IJsonSerializationContext)context;
            JsonObject child = CoreSerializer.SerializeToJsonObject(
                new MigratingLeaf("7.0.0"));
            // A nested sealed contract has no discriminator in its serialized representation.
            child.Remove("$type");
            jsonContext.SetNode(
                "MigrationAwareChild",
                typeof(MigratingLeaf),
                typeof(MigratingLeaf),
                child);
        }
    }

    private static T CreateMigrated<T>(T source)
        where T : ICoreSerializable
    {
        return (T)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(source),
            typeof(T));
    }
}
