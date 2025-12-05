using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

public class FontConfigTests
{
    [Test]
    public void Deserialize_SynchronizesFontDirectories()
    {
        var cfg = new FontConfig();
        string[] newDirs =
        {
            Path.Combine(ArtifactProvider.GetArtifactDirectory(), "Fonts1"),
            Path.Combine(ArtifactProvider.GetArtifactDirectory(), "Fonts2"),
        };

        var json = new JsonObject
        {
            ["FontDirectories"] = new JsonArray(newDirs.Select(d => (JsonNode)d).ToArray())
        };

        var ctx = new JsonSerializationContext(typeof(FontConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            cfg.Deserialize(ctx);
        }

        Assert.That(cfg.FontDirectories.ToArray(), Is.EqualTo(newDirs));
    }
}
