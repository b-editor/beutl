using System.Linq;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

public class EditorConfigTests
{
    [Test]
    public void SerializeDeserialize_LibraryTabDisplayModes()
    {
        var cfg = new EditorConfig();
        cfg.LibraryTabDisplayModes.Clear();
        cfg.LibraryTabDisplayModes["Custom1"] = LibraryTabDisplayMode.Hide;
        cfg.LibraryTabDisplayModes["Custom2"] = LibraryTabDisplayMode.Show;

        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(EditorConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            cfg.Serialize(ctx);
        }

        // Clear and restore into a new instance
        var cfg2 = new EditorConfig();
        var ctx2 = new JsonSerializationContext(typeof(EditorConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx2))
        {
            cfg2.Deserialize(ctx2);
        }

        Assert.That(cfg2.LibraryTabDisplayModes.ToArray(), Is.EquivalentTo(cfg.LibraryTabDisplayModes.ToArray()));
    }
}
