using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class JsonDeepCloneTests
{
    [Test]
    public void CopyTo_Object_ProducesIndependentClone()
    {
        var src = new JsonObject
        {
            ["a"] = 1,
            ["b"] = new JsonObject { ["c"] = "x" },
            ["d"] = new JsonArray(1, 2, 3)
        };
        var dest = new JsonObject();
        JsonDeepClone.CopyTo(src, dest);

        // Mutate source and ensure dest is unaffected
        ((JsonObject)src["b"]!).Remove("c");
        ((JsonArray)src["d"]!).Add(4);

        Assert.That(dest["a"]!.ToJsonString(), Is.EqualTo("1"));
        Assert.That(((JsonObject)dest["b"]!)["c"]!.ToJsonString(), Is.EqualTo("\"x\""));
        Assert.That(((JsonArray)dest["d"]!).Count, Is.EqualTo(3));
    }

    [Test]
    public void CopyTo_Array_ProducesIndependentClone()
    {
        var src = new JsonArray
        {
            1, new JsonObject { ["k"] = "v" }, new JsonArray(10)
        };
        var dest = new JsonArray();
        JsonDeepClone.CopyTo(src, dest);

        ((JsonObject)src[1]!).Remove("k");
        ((JsonArray)src[2]!).Add(20);

        Assert.That(((JsonObject)dest[1]!).ContainsKey("k"), Is.True);
        Assert.That(((JsonArray)dest[2]!).Count, Is.EqualTo(1));
    }
}

