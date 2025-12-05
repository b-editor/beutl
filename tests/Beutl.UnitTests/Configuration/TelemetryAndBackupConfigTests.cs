using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

public class TelemetryAndBackupConfigTests
{
    [Test]
    public void Telemetry_PropertyChange_RaisesConfigurationChanged()
    {
        var cfg = new TelemetryConfig();
        int changed = 0;
        cfg.ConfigurationChanged += (_, _) => changed++;

        cfg.Beutl_Logging = true;
        Assert.That(changed, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Backup_PropertyChange_RaisesConfigurationChanged()
    {
        var cfg = new BackupConfig();
        int changed = 0;
        cfg.ConfigurationChanged += (_, _) => changed++;

        cfg.BackupSettings = !cfg.BackupSettings;
        Assert.That(changed, Is.GreaterThanOrEqualTo(1));
    }
}

