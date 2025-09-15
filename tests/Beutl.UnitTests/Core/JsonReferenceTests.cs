using System;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class JsonReferenceTests
{
    private sealed class Obj : CoreObject {}

    [Test]
    public void Reference_SerializesAsGuid_AndDeserializesToReference()
    {
        var id = Guid.NewGuid();
        var r = new Reference<Obj>(id);
        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            ctx.SetValue("ref", r);
        }

        Assert.That(json["ref"]!.ToJsonString().Trim('"'), Is.EqualTo(id.ToString()));

        var ctx2 = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx2))
        {
            var restored = ctx2.GetValue<Reference<Obj>>("ref");
            Assert.That(restored.IsNull, Is.False);
            Assert.That((Guid)restored, Is.EqualTo(id));
            Assert.That((Obj?)restored, Is.Null);
        }
    }
}

