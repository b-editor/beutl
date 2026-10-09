using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class ViewConfigPinnedToolTabsTests
{
    [Test]
    public void Pinned_tool_tabs_round_trip_in_pin_order()
    {
        var source = new ViewConfig();
        source.PinnedToolTabs.Add("Beutl.Services.PrimitiveImpls.HistoryTabExtension");
        source.PinnedToolTabs.Add("Beutl.Editor.Components.TerminalTab.TerminalTabExtension");

        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = new ViewConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(
            restored.PinnedToolTabs,
            Is.EqualTo(new[]
            {
                "Beutl.Services.PrimitiveImpls.HistoryTabExtension",
                "Beutl.Editor.Components.TerminalTab.TerminalTabExtension",
            }));
    }

    [Test]
    public void Settings_written_before_pins_existed_load_with_no_pins()
    {
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, new JsonObject { ["RecentFiles"] = new JsonArray() });

        Assert.That(config.PinnedToolTabs, Is.Empty);
    }

    [Test]
    public void Pinning_raises_ConfigurationChanged_so_the_settings_are_saved()
    {
        var config = new ViewConfig();
        int changes = 0;
        config.ConfigurationChanged += (_, _) => changes++;

        config.PinnedToolTabs.Add("Beutl.Services.PrimitiveImpls.HistoryTabExtension");
        config.PinnedToolTabs.Remove("Beutl.Services.PrimitiveImpls.HistoryTabExtension");

        Assert.That(changes, Is.EqualTo(2));
    }
}
