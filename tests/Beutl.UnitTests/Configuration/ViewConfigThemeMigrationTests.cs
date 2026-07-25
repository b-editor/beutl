using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class ViewConfigThemeMigrationTests
{
    // ViewConfig's DefaultThemeId is private and the app-layer DarkBorderThemeExtension.ThemeId is out
    // of reach here, so the id is repeated; DarkBorderThemeExtensionTests.ViewConfigDefault_MatchesThemeId
    // is what keeps all three in sync.
    private const string DefaultThemeId = "beutl.dark.border";

    // Legacy <2.0 settings.json stored the old ViewTheme enum as an int (0-3) or PascalCase name.
    // The string id persisted by >=2.0 and unknown ids (custom themes) must round-trip unchanged.
    // Legacy Dark (1) is the exception: it was the pre-2.0 default, so it means "never chose a theme"
    // and migrates onto the current default rather than pinning the user to the classic look. A name
    // string is an id, not an enum, so "dark"/"Dark" is an explicit opt-in to classic and is kept.
    [TestCase("0", BuiltinThemeIds.Light)]
    [TestCase("1", DefaultThemeId)]
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
    [TestCase(1, DefaultThemeId)]
    [TestCase(2, BuiltinThemeIds.HighContrast)]
    [TestCase(3, BuiltinThemeIds.System)]
    public void MigratesLegacyThemeValue_AsNumber(int raw, string expected)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(raw) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(expected));
    }

    // Never a ViewTheme member, so this is corrupt rather than a choice and lands where an absent
    // value does — the two must not diverge.
    [TestCase(4)]
    [TestCase(-1)]
    [TestCase(99)]
    public void UsesDefaultTheme_WhenLegacyEnumIsOutOfRange(int raw)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(raw) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(DefaultThemeId));
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

        // Round-trip through JSON *text*, the way settings.json actually persists: escaping only
        // happens on write, so handing the in-memory JsonObject straight back would skip it.
        JsonObject serialized = CoreSerializer.SerializeToJsonObject(config);
        var json = JsonNode.Parse(serialized.ToJsonString())!.AsObject();
        var restored = new ViewConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.Theme, Is.EqualTo(themeId));
    }

    // Only 0-3 were ever ViewTheme members. A custom id that merely looks numeric is a normal id and
    // must not be swallowed by the legacy-enum path.
    [TestCase("2026")]
    [TestCase("4")]
    [TestCase("-1")]
    public void KeepsNumericLookingCustomThemeId(string themeId)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(themeId) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(themeId));
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

    // No persisted choice must land on the same theme a fresh install gets, or a settings.json written
    // before the key existed would strand its user on a different look than ViewConfig's DefaultValue.
    [Test]
    public void UsesDefaultTheme_WhenThemeNull()
    {
        var json = new JsonObject { ["Theme"] = null };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(DefaultThemeId));
    }

    [Test]
    public void UsesDefaultTheme_WhenThemeMissing()
    {
        var json = new JsonObject();
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(DefaultThemeId));
    }

    // A hand-edited or corrupted file can put a container where the id belongs; it carries no choice,
    // so it resolves like a missing key rather than falling to a hard-coded built-in.
    [Test]
    public void UsesDefaultTheme_WhenThemeIsNotAJsonValue()
    {
        var json = new JsonObject { ["Theme"] = new JsonObject() };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(DefaultThemeId));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void UsesDefaultTheme_WhenThemeIsBlank(string raw)
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(raw) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(DefaultThemeId));
    }

    // The classic look stays reachable: picking "Dark (Classic)" persists this id, and reloading must
    // not migrate it back onto the default the way legacy enum 1 is migrated.
    [Test]
    public void KeepsExplicitClassicDarkId()
    {
        var config = new ViewConfig { Theme = BuiltinThemeIds.Dark };

        JsonObject json = CoreSerializer.SerializeToJsonObject(config);
        var restored = new ViewConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.Theme, Is.EqualTo(BuiltinThemeIds.Dark));
    }

    [Test]
    public void KeepsExplicitDefaultThemeId()
    {
        var json = new JsonObject { ["Theme"] = JsonValue.Create(DefaultThemeId) };
        var config = new ViewConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.Theme, Is.EqualTo(DefaultThemeId));
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
