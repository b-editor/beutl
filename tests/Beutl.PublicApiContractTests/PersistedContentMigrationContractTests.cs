using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class PersistedContentMigrationContractTests : PublicApiContractTestBase
{
    [Test]
    public void Plugin_project_items_and_elements_can_report_required_migration_versions()
    {
        AssertDoesNotHaveFriendAccess(typeof(Project).Assembly);
        AssertDoesNotHaveFriendAccess(typeof(Element).Assembly);
        var project = new Project();
        var scene = new MigratingSceneItem();
        project.Items.Add(scene);
        scene.AddChild(new MigratingElement());

        CoreSerializer.SerializeToJsonObject(project);

        Assert.That(project.MinAppVersion, Is.EqualTo("8.0.0"));
    }

    private sealed class MigratingSceneItem : Scene
    {
        public MigratingSceneItem()
        {
            ReportPersistedContentMigration("7.0.0");
        }
    }

    private sealed class MigratingElement : Element
    {
        public MigratingElement()
        {
            ReportPersistedContentMigration("8.0.0");
        }
    }
}
