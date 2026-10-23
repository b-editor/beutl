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
            Assert.That(config.EnableForNewProjects.Value, Is.True);
            Assert.That(config.AutoCommitOnSave.Value, Is.True);
            Assert.That(config.AutoCommitOnClose.Value, Is.True);
            Assert.That(config.GitExecutablePath.Value, Is.Null);
            Assert.That(config.UseLfsWhenAvailable.Value, Is.True);
            Assert.That(config.LargeMediaWarningThresholdMb.Value, Is.EqualTo(50));
        });
    }

    [Test]
    public void Serialization_roundtrips_all_values()
    {
        var source = new VersionControlConfig
        {
            EnableForNewProjects = { Value = false },
            AutoCommitOnSave = { Value = false },
            AutoCommitOnClose = { Value = false },
            GitExecutablePath = { Value = "/opt/git/bin/git" },
            UseLfsWhenAvailable = { Value = false },
            LargeMediaWarningThresholdMb = { Value = 125 },
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = new VersionControlConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.EnableForNewProjects.Value, Is.False);
            Assert.That(restored.AutoCommitOnSave.Value, Is.False);
            Assert.That(restored.AutoCommitOnClose.Value, Is.False);
            Assert.That(restored.GitExecutablePath.Value, Is.EqualTo("/opt/git/bin/git"));
            Assert.That(restored.UseLfsWhenAvailable.Value, Is.False);
            Assert.That(restored.LargeMediaWarningThresholdMb.Value, Is.EqualTo(125));
        });
    }

    [Test]
    public void Property_change_raises_ConfigurationChanged()
    {
        var config = new VersionControlConfig();
        int raised = 0;
        config.ConfigurationChanged += (_, _) => raised++;

        config.AutoCommitOnSave.Value = false;

        Assert.That(raised, Is.EqualTo(1));
    }
}
