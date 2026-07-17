using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class ViewConfigThemeMigrationTests
{
    // Legacy <2.0 settings.json stored the old ViewTheme enum as an int (0-3) or PascalCase name.
    // The string id persisted by >=2.0 and unknown ids (custom themes) must round-trip unchanged.
    [TestCase("0", BuiltinThemeIds.Light)]
    [TestCase("1", BuiltinThemeIds.Dark)]
    [TestCase("2", BuiltinThemeIds.HighContrast)]
    [TestCase("3", BuiltinThemeIds.System)]
    [TestCase("Light", BuiltinThemeIds.Light)]
    [TestCase("Dark", BuiltinThemeIds.Dark)]
    [TestCase("HighContrast", BuiltinThemeIds.HighContrast)]
    [TestCase("System", BuiltinThemeIds.System)]
    [TestCase("light", BuiltinThemeIds.Light)]
    [TestCase("dark", BuiltinThemeIds.Dark)]
    [TestCase("highcontrast", BuiltinThemeIds.HighContrast)]
    [TestCase("system", BuiltinThemeIds.System)]
    [TestCase("plugin.solarized", "plugin.solarized")]
    public void MigratesLegacyThemeValue_AsString(string raw, string expected)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(raw) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(expected));
    }

    [TestCase(0, BuiltinThemeIds.Light)]
    [TestCase(1, BuiltinThemeIds.Dark)]
    [TestCase(2, BuiltinThemeIds.HighContrast)]
    [TestCase(3, BuiltinThemeIds.System)]
    public void MigratesLegacyThemeValue_AsNumber(int raw, string expected)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(raw) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(expected));
    }

    // A custom id survives a JSON round-trip only if it is decoded rather than read as raw JSON
    // text: System.Text.Json escapes these on write, and the escapes must not reach the registry.
    [TestCase("plugin.\"quoted\"")]
    [TestCase("plugin.back\\slash")]
    [TestCase("plugin.日本語")]
    [TestCase("plugin.tab\there")]
    public void DecodesEscapedCustomThemeId(string themeId)
    {
        var config = new ViewConfig { Theme = themeId };

        JsonObject json = CoreSerializer.SerializeToJsonObject(config);
        var restored = new ViewConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.Theme, Is.EqualTo(themeId));
    }

    [TestCase("  dark  ", BuiltinThemeIds.Dark)]
    [TestCase("\tSystem\n", BuiltinThemeIds.System)]
    [TestCase(" 2 ", BuiltinThemeIds.HighContrast)]
    public void NormalizesWhitespacePaddedThemeId(string raw, string expected)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(raw) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(expected));
    }

    [Test]
    public void DefaultsToDark_WhenThemeNull()
    {
        var json = new JsonObject { ["Theme"] = null };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(BuiltinThemeIds.Dark));
    }

    [Test]
    public void DefaultsToDark_WhenThemeMissing()
    {
        var json = new JsonObject();
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(BuiltinThemeIds.Dark));
    }

    [Test]
    public void RoundTripsCustomThemeId()
    {
        var config = new ViewConfig { Theme = "plugin.custom" };

        JsonObject json = CoreSerializer.SerializeToJsonObject(config);
        var restored = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.Theme, Is.EqualTo("plugin.custom"));
    }
}
