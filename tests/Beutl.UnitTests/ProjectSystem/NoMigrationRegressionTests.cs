using System.Text;
using System.Text.Json.Nodes;
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

        restored.Items.Add(new MigratedProjectItem("4.2.0"));
        restored.Items.Add(new MigratedProjectItem("5.1.0"));

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
        scene.AddChild(new MigratedElement("6.0.0")
        {
            Uri = new Uri(Path.Combine(_tempDirectory, "element.belm")),
        });

        JsonObject json = CoreSerializer.SerializeToJsonObject(project);

        Assert.Multiple(() =>
        {
            Assert.That(project.MinAppVersion, Is.EqualTo("6.0.0"));
            Assert.That((string?)json["minAppVersion"], Is.EqualTo("6.0.0"));
        });
    }

    [Test]
    public void Deserialization_aggregates_non_hierarchical_serialized_child_migrations()
    {
        var source = new MigratingContainer
        {
            Child = new MigratingLeaf(),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(source);

        var restored = (MigratingContainer)CoreSerializer.DeserializeFromJsonObject(
            json,
            typeof(ProjectItem));
        var project = new Project();
        project.Items.Add(restored);

        Assert.That(project.MinAppVersion, Is.EqualTo("7.0.0"));
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
        public MigratedProjectItem(string requiredVersion)
        {
            ReportPersistedContentMigration(requiredVersion);
        }
    }

    private sealed class MigratedElement : Element
    {
        public MigratedElement(string requiredVersion)
        {
            ReportPersistedContentMigration(requiredVersion);
        }
    }

    private sealed class MigratingContainer : ProjectItem
    {
        public MigratingLeaf? Child { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Child), Child);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Child = context.GetValue<MigratingLeaf>(nameof(Child));
        }
    }

    private sealed class MigratingLeaf : CoreObject
    {
        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            ReportPersistedContentMigration("7.0.0");
        }
    }
}
