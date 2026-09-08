using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class EditorConfigLibraryTabDisplayModesTests
{
    [Test]
    public void Deserialize_PreservesSavedValues_AndRestoresMissingDefaults()
    {
        // A config saved before a tab existed lacks its entry; the deserializer must
        // keep the saved choices while restoring the missing defaults, or the per-tab
        // visibility binding resolves nothing.
        var json = new JsonObject
        {
            ["LibraryTabDisplayModes"] = new JsonObject
            {
                ["Nodes"] = 0, // Show, overriding the default Hide
                // "Search" is absent on purpose — its default must be restored.
            },
        };
        var config = new EditorConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.Multiple(() =>
        {
            Assert.That(config.LibraryTabDisplayModes["Nodes"], Is.EqualTo(LibraryTabDisplayMode.Show));
            Assert.That(config.LibraryTabDisplayModes["Search"], Is.EqualTo(LibraryTabDisplayMode.Show));
            Assert.That(config.LibraryTabDisplayModes["Library"], Is.EqualTo(LibraryTabDisplayMode.Show));
            Assert.That(config.LibraryTabDisplayModes["Easings"], Is.EqualTo(LibraryTabDisplayMode.Show));
        });
    }

    [Test]
    public void Deserialize_SavedHide_OverridesTheDefault()
    {
        var json = new JsonObject
        {
            ["LibraryTabDisplayModes"] = new JsonObject
            {
                ["Library"] = 1, // Hide, overriding the default Show
            },
        };
        var config = new EditorConfig();

        CoreSerializer.PopulateFromJsonObject(config, json);

        Assert.That(config.LibraryTabDisplayModes["Library"], Is.EqualTo(LibraryTabDisplayMode.Hide));
    }
}
