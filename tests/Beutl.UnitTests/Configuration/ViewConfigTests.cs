using System;
using System.Linq;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

public class ViewConfigTests
{
    [Test]
    public void UpdateRecentFile_MovesToFront_NoDuplicates_RaisesEvent()
    {
        var cfg = new ViewConfig();
        int changed = 0;
        cfg.ConfigurationChanged += (_, _) => changed++;

        cfg.UpdateRecentFile("a");
        cfg.UpdateRecentFile("b");
        cfg.UpdateRecentFile("a");

        Assert.That(cfg.RecentFiles.Count, Is.EqualTo(2));
        Assert.That(cfg.RecentFiles[0], Is.EqualTo("a"));
        Assert.That(cfg.RecentFiles[1], Is.EqualTo("b"));
        Assert.That(changed, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void SerializeDeserialize_WindowAndRecents()
    {
        var cfg = new ViewConfig
        {
            WindowPosition = (100, 200),
            WindowSize = (1280, 720),
            IsWindowMaximized = true,
            UseCustomAccentColor = true,
            CustomAccentColor = "#112233",
        };
        cfg.UpdateRecentFile("file1");
        cfg.UpdateRecentFile("file2");
        cfg.UpdateRecentProject("proj1");
        cfg.UpdateRecentProject("proj2");

        var json = new JsonObject();
        var ser = new JsonSerializationContext(typeof(ViewConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ser))
        {
            cfg.Serialize(ser);
        }

        var cfg2 = new ViewConfig();
        var deser = new JsonSerializationContext(typeof(ViewConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(deser))
        {
            cfg2.Deserialize(deser);
        }

        Assert.That(cfg2.WindowPosition, Is.EqualTo((100, 200)));
        Assert.That(cfg2.WindowSize, Is.EqualTo((1280, 720)));
        Assert.That(cfg2.IsWindowMaximized, Is.True);
        Assert.That(cfg2.UseCustomAccentColor, Is.True);
        Assert.That(cfg2.CustomAccentColor, Is.EqualTo("#112233"));
        Assert.That(cfg2.RecentFiles.ToArray(), Is.EqualTo(cfg.RecentFiles.ToArray()));
        Assert.That(cfg2.RecentProjects.ToArray(), Is.EqualTo(cfg.RecentProjects.ToArray()));
    }

    [Test]
    public void PropertyChange_RaisesConfigurationChanged()
    {
        var cfg = new ViewConfig();
        int changed = 0;
        cfg.ConfigurationChanged += (_, _) => changed++;

        cfg.Theme = cfg.Theme == ViewConfig.ViewTheme.Dark ? ViewConfig.ViewTheme.Light : ViewConfig.ViewTheme.Dark;
        Assert.That(changed, Is.GreaterThanOrEqualTo(1));
    }
}
