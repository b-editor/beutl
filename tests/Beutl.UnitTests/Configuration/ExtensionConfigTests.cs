using System.Linq;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

public class ExtensionConfigTests
{
    [Test]
    public void SerializeDeserialize_RoundTrip()
    {
        var cfg = new ExtensionConfig();
        cfg.EditorExtensions[".txt"] = new Beutl.Collections.CoreList<ExtensionConfig.TypeLazy>(
            new[] { new ExtensionConfig.TypeLazy("[System]System:Int32"), new ExtensionConfig.TypeLazy("[System]System:String") });
        cfg.DecoderPriority.Add(new ExtensionConfig.TypeLazy("[System]System:Double"));

        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(ExtensionConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            cfg.Serialize(ctx);
        }

        var cfg2 = new ExtensionConfig();
        var ctx2 = new JsonSerializationContext(typeof(ExtensionConfig), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx2))
        {
            cfg2.Deserialize(ctx2);
        }

        Assert.That(cfg2.EditorExtensions.ContainsKey(".txt"), Is.True);
        Assert.That(cfg2.EditorExtensions[".txt"].Select(x => x.FormattedTypeName).ToArray(),
                    Is.EqualTo(cfg.EditorExtensions[".txt"].Select(x => x.FormattedTypeName).ToArray()));

        Assert.That(cfg2.DecoderPriority.Select(x => x.FormattedTypeName).ToArray(),
                    Is.EqualTo(cfg.DecoderPriority.Select(x => x.FormattedTypeName).ToArray()));
    }
}
