using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class JsonDeepCloneTests
{
    [Test]
    public void CopyTo_Object_CopiesAllKeys()
    {
        var src = new JsonObject
        {
            ["a"] = 1,
            ["b"] = "hello",
            ["c"] = new JsonObject { ["x"] = true },
        };
        var dst = new JsonObject();

        JsonDeepClone.CopyTo(src, dst);

        Assert.Multiple(() =>
        {
            Assert.That((int?)dst["a"], Is.EqualTo(1));
            Assert.That((string?)dst["b"], Is.EqualTo("hello"));
            Assert.That((bool?)dst["c"]?["x"], Is.True);
        });
    }

    [Test]
    public void CopyTo_Object_DeepClonesChildren()
    {
        var inner = new JsonObject { ["v"] = 100 };
        var src = new JsonObject { ["inner"] = inner };
        var dst = new JsonObject();

        JsonDeepClone.CopyTo(src, dst);

        // 元のオブジェクトを書き換えてもコピー先には影響しない
        inner["v"] = 999;

        Assert.That((int?)dst["inner"]?["v"], Is.EqualTo(100));
    }

    [Test]
    public void CopyTo_Object_HandlesNullValues()
    {
        var src = new JsonObject { ["nullable"] = null };
        var dst = new JsonObject();

        JsonDeepClone.CopyTo(src, dst);

        Assert.That(dst.ContainsKey("nullable"), Is.True);
        Assert.That(dst["nullable"], Is.Null);
    }
}
