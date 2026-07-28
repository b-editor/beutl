using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class VersionControlConfigTests
{
    [Test]
    public void Defaults_match_version_control_policy()
    {
        var config = new VersionControlConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.EnableForNewProjects, Is.True);
            Assert.That(config.AutoCommitOnSave, Is.True);
            Assert.That(config.AutoCommitOnClose, Is.True);
            Assert.That(config.GitExecutablePath, Is.Null);
            Assert.That(config.UseLfsWhenAvailable, Is.True);
            Assert.That(config.LargeMediaWarningThresholdMb, Is.EqualTo(50));
            Assert.That(GlobalConfiguration.Instance.VersionControlConfig, Is.Not.Null);
        });
    }

    [Test]
    public void Serialization_roundtrips_all_values()
    {
        var source = new VersionControlConfig
        {
            EnableForNewProjects = false,
            AutoCommitOnSave = false,
            AutoCommitOnClose = false,
            GitExecutablePath = "/opt/git/bin/git",
            UseLfsWhenAvailable = false,
            LargeMediaWarningThresholdMb = 125,
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = new VersionControlConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.EnableForNewProjects, Is.False);
            Assert.That(restored.AutoCommitOnSave, Is.False);
            Assert.That(restored.AutoCommitOnClose, Is.False);
            Assert.That(restored.GitExecutablePath, Is.EqualTo("/opt/git/bin/git"));
            Assert.That(restored.UseLfsWhenAvailable, Is.False);
            Assert.That(restored.LargeMediaWarningThresholdMb, Is.EqualTo(125));
        });
    }

    [Test]
    public void Property_change_raises_ConfigurationChanged()
    {
        var config = new VersionControlConfig();
        int raised = 0;
        config.ConfigurationChanged += (_, _) => raised++;

        config.AutoCommitOnSave = false;

        Assert.That(raised, Is.EqualTo(1));
    }
}
