using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Serialization;

namespace Beutl.UnitTests.Api;

// extensionsSettings.json keys each extension's settings by the full name of its type, so a renamed or moved
// extension reads the settings saved under a former name (FormerPersistedNameAttribute) when there are none under
// the current one, and saves under the current one.
[TestFixture]
public class ExtensionSettingsStoreTests
{
    private const string FormerKey = "Former.Namespace.FormerExtension";

    [Test]
    public void Restore_SettingsUnderAFormerTypeName_ApplyWhenTheCurrentNameHasNone()
    {
        var json = new JsonObject { [FormerKey] = new JsonObject { [nameof(CountSettings.Count)] = 3 } };
        var store = new ExtensionSettingsStore(json, persist: false);
        var extension = new RenamedExtension();

        store.Restore(extension, extension.Settings);
        store.Save(extension, extension.Settings);

        Assert.Multiple(() =>
        {
            Assert.That(extension.Settings.Count.Value, Is.EqualTo(3));
            Assert.That(
                (int?)json[typeof(RenamedExtension).FullName!]?[nameof(CountSettings.Count)],
                Is.EqualTo(3),
                "Saving writes the current name.");
        });
    }

    [Test]
    public void Restore_SettingsUnderTheCurrentName_WinOverAFormerName()
    {
        var json = new JsonObject
        {
            [typeof(RenamedExtension).FullName!] = new JsonObject { [nameof(CountSettings.Count)] = 1 },
            [FormerKey] = new JsonObject { [nameof(CountSettings.Count)] = 3 },
        };
        var store = new ExtensionSettingsStore(json, persist: false);
        var extension = new RenamedExtension();

        store.Restore(extension, extension.Settings);

        Assert.That(extension.Settings.Count.Value, Is.EqualTo(1));
    }

    [FormerPersistedName("[Beutl.UnitTests]Former.Namespace:FormerExtension")]
    private sealed class RenamedExtension : Extension
    {
        public override CountSettings Settings { get; } = new();
    }

    private sealed class CountSettings : ExtensionSettings
    {
        public CountSettings()
        {
            ScanProperties<CountSettings>();
        }

        public IProperty<int> Count { get; } = new ValueOnlyProperty<int>(default);
    }
}
