using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class JsonSerializationCollectionsTests
{
    private sealed class Node : CoreObject
    {
        public string Value { get; set; } = string.Empty;

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(Value), Value);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            Value = context.GetValue<string>(nameof(Value)) ?? string.Empty;
        }
    }

    [Test]
    public void DictionaryOfCoreObjects_SerializesToObject_AndRoundTrips()
    {
        var dict = new Dictionary<string, Node>
        {
            ["a"] = new Node { Value = "A" },
            ["b"] = new Node { Value = "B" },
        };

        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            ctx.SetValue("dict", dict);
        }

        Assert.That(json["dict"], Is.InstanceOf<JsonObject>());
        var readCtx = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(readCtx))
        {
            var restored = readCtx.GetValue<Dictionary<string, Node>>("dict");
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!["a"].Value, Is.EqualTo("A"));
            Assert.That(restored["b"].Value, Is.EqualTo("B"));
        }
    }

    [Test]
    public void ArrayOfCoreObjects_RoundTrips()
    {
        var arr = new[] { new Node { Value = "X" }, new Node { Value = "Y" } };
        var json = new JsonObject();
        var ctx = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(ctx))
        {
            ctx.SetValue("arr", arr);
        }

        var readCtx = new JsonSerializationContext(typeof(object), NullSerializationErrorNotifier.Instance, json: json);
        using (ThreadLocalSerializationContext.Enter(readCtx))
        {
            var restored = readCtx.GetValue<Node[]>("arr");
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Length, Is.EqualTo(2));
            Assert.That(restored[0].Value, Is.EqualTo("X"));
            Assert.That(restored[1].Value, Is.EqualTo("Y"));
        }
    }
}

