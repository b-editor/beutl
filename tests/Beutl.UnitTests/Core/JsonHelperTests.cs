using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class JsonHelperTests
{
    [Test]
    public void JsonSave_AndRestore_RoundTripsContent()
    {
        var json = new JsonObject { ["x"] = 1, ["s"] = "hello" };
        string path = Path.GetTempFileName();
        try
        {
            json.JsonSave(path);

            JsonNode? restored = JsonHelper.JsonRestore(path);
            Assert.That(restored, Is.Not.Null);
            Assert.That((int)restored!["x"]!, Is.EqualTo(1));
            Assert.That((string?)restored["s"], Is.EqualTo("hello"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void JsonSave_DoesNotLeaveTempFile()
    {
        var json = new JsonObject { ["v"] = 1 };
        string path = Path.GetTempFileName();
        string dir = Path.GetDirectoryName(path)!;
        string baseName = Path.GetFileName(path);
        try
        {
            json.JsonSave(path);

            string[] tmps = Directory.GetFiles(dir, $"{baseName}.*.tmp");
            Assert.That(tmps, Is.Empty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void JsonRestore_FileNotFound_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        JsonNode? result = JsonHelper.JsonRestore(path);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void JsonRestore_InvalidJson_ReturnsNull()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{ this is not valid json");

            JsonNode? result = JsonHelper.JsonRestore(path);

            Assert.That(result, Is.Null);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void JsonRestore_InvalidJson_LeavesFileUntouched()
    {
        string path = Path.GetTempFileName();
        const string corrupt = "{ this is not valid json";
        try
        {
            File.WriteAllText(path, corrupt);

            _ = JsonHelper.JsonRestore(path);

            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.ReadAllText(path), Is.EqualTo(corrupt));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void TryGetDiscriminator_Type_ReturnsTypeForKnownEntries()
    {
        var node = new JsonObject { ["$type"] = TypeFormat.ToString(typeof(Optional<int>)) };

        bool ok = node.TryGetDiscriminator(out Type? type);

        Assert.That(ok, Is.True);
        Assert.That(type, Is.EqualTo(typeof(Optional<int>)));
    }

    [Test]
    public void TryGetDiscriminator_String_ReturnsRawValue()
    {
        var node = new JsonObject { ["@type"] = "[Some.Asm]:Some.Type" };

        bool ok = node.TryGetDiscriminator(out string? text);

        Assert.That(ok, Is.True);
        Assert.That(text, Is.EqualTo("[Some.Asm]:Some.Type"));
    }

    [Test]
    public void TryGetDiscriminator_NoTypeProperty_ReturnsFalse()
    {
        var node = new JsonObject { ["foo"] = 1 };

        bool ok = node.TryGetDiscriminator(out Type? type);

        Assert.That(ok, Is.False);
        Assert.That(type, Is.Null);
    }

    [Test]
    public void GetDiscriminator_FallsBackToFallbackTypeAttribute()
    {
        // No discriminator on node, BaseType has FallbackTypeAttribute -> uses fallback.
        var node = new JsonObject();

        Type? type = node.GetDiscriminator(typeof(BaseWithFallback));

        Assert.That(type, Is.EqualTo(typeof(FallbackImpl)));
    }

    [Test]
    public void GetDiscriminator_PrefersExplicitTypeOverFallback()
    {
        var node = new JsonObject { ["$type"] = TypeFormat.ToString(typeof(Optional<int>)) };

        Type? type = node.GetDiscriminator(typeof(BaseWithFallback));

        Assert.That(type, Is.EqualTo(typeof(Optional<int>)));
    }

    [Test]
    public void WriteDiscriminator_WritesTypeFormatString()
    {
        var node = new JsonObject();

        node.WriteDiscriminator(typeof(Optional<int>));

        Assert.That((string?)node["$type"], Is.EqualTo(TypeFormat.ToString(typeof(Optional<int>))));
    }

    [Test]
    public void TryGetPropertyValueAsJsonValue_TypedAccessSucceeds()
    {
        var node = new JsonObject { ["v"] = 42 };

        bool ok = node.TryGetPropertyValueAsJsonValue<int>("v", out int value);

        Assert.That(ok, Is.True);
        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void TryGetPropertyValueAsJsonValue_MissingProperty_ReturnsFalse()
    {
        var node = new JsonObject();

        bool ok = node.TryGetPropertyValueAsJsonValue<int>("v", out int value);

        Assert.That(ok, Is.False);
        Assert.That(value, Is.EqualTo(0));
    }

    [Test]
    public void ToDictionary_FlattensJsonStructure()
    {
        var node = new JsonObject
        {
            ["i"] = 1,
            ["s"] = "x",
            ["b"] = true,
            ["arr"] = new JsonArray(1, 2, 3),
            ["nested"] = new JsonObject { ["a"] = 1 },
        };

        Dictionary<string, object> dict = node.ToDictionary();

        Assert.That(dict["s"], Is.EqualTo("x"));
        Assert.That(dict["b"], Is.EqualTo(true));
        Assert.That(dict["arr"], Is.InstanceOf<object[]>());
        Assert.That(dict["nested"], Is.InstanceOf<Dictionary<string, object>>());
    }

    [Test]
    public void GetOrCreateConverterInstance_ReturnsSameInstanceForSameType()
    {
        var c1 = JsonHelper.GetOrCreateConverterInstance(typeof(OptionalJsonConverter));
        var c2 = JsonHelper.GetOrCreateConverterInstance(typeof(OptionalJsonConverter));

        Assert.That(c1, Is.SameAs(c2));
        Assert.That(c1, Is.InstanceOf<OptionalJsonConverter>());
    }

    [FallbackType(typeof(FallbackImpl))]
    private abstract class BaseWithFallback
    {
    }

    private sealed class FallbackImpl : BaseWithFallback
    {
    }
}
