using System.Text.Json.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class PersistedContentMigrationContractTests : PublicApiContractTestBase
{
    [Test]
    public void Plugin_serializable_types_can_report_required_migration_versions()
    {
        AssertDoesNotHaveFriendAccess(typeof(Project).Assembly);
        AssertDoesNotHaveFriendAccess(typeof(Element).Assembly);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"migration-contract-{Guid.NewGuid():N}");
        var source = new MigratingSceneItem
        {
            Uri = new Uri(Path.Combine(root, "scene.scene")),
            Child = new MigratingLeaf(),
        };
        var options = new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(source, options);
        var restored = (MigratingSceneItem)CoreSerializer.DeserializeFromJsonObject(
            json,
            typeof(ProjectItem), options);
        var project = new Project { Uri = new Uri(Path.Combine(root, "project.bep")) };
        project.Items.Add(restored);
        var manualContext = new JsonSerializationContext(typeof(MigratingLeaf));

        Assert.Multiple(() =>
        {
            Assert.That(project.MinAppVersion, Is.EqualTo("8.0.0"));
            Assert.DoesNotThrow(() => new MigratingLeaf().Deserialize(manualContext));
        });
    }

    private sealed class MigratingSceneItem : Scene
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
            context.ReportPersistedContentMigration("7.0.0");
            Child = context.GetValue<MigratingLeaf>(nameof(Child));
        }
    }

    private sealed class MigratingLeaf : ICoreSerializable
    {
        public void Serialize(ICoreSerializationContext context)
        {
        }

        public void Deserialize(ICoreSerializationContext context)
        {
            context.ReportPersistedContentMigration("8.0.0");
        }
    }
}
