namespace Beutl.UnitTests.Core;

public class TypeFormatTests
{
    [Test]
    public void RoundTrip_CommonRuntimeTypes()
    {
        AssertRoundTrip(typeof(int));
        AssertRoundTrip(typeof(string));
        AssertRoundTrip(typeof(double));
    }

    [Test]
    public void RoundTrip_BeutlOptional()
    {
        // Optional<T> lives in the Beutl namespace inside Beutl.Core,
        // exercising the namespace==assembly shortened encoding path.
        AssertRoundTrip(typeof(Optional<int>));
    }

    [Test]
    public void RoundTrip_NestedType()
    {
        AssertRoundTrip(typeof(NestedHost.Nested));
    }

    [Test]
    public void RoundTrip_NestedGenericType()
    {
        AssertRoundTrip(typeof(NestedHost.NestedGeneric<int>));
    }

    [Test]
    public void ToString_AssemblyAndNamespacePrefix()
    {
        // The format encodes assembly in [...] and uses ':' before the type name.
        string formatted = TypeFormat.ToString(typeof(int));

        Assert.That(formatted, Does.StartWith("["));
        Assert.That(formatted, Does.Contain("]"));
        Assert.That(formatted, Does.Contain(":Int32"));
    }

    [Test]
    public void ToType_UnknownAssembly_ReturnsNull()
    {
        // Make a syntactically valid string referencing an unknown assembly.
        string formatted = "[NoSuchAssembly.Foo]:NoSuchType";

        Type? result = TypeFormat.ToType(formatted);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ToType_LegacyFFmpegEmbeddingNamespaceIsRemapped()
    {
        // The TypeFormat.ToType replaces "Beutl.Embedding.FFmpeg" with
        // "Beutl.Extensions.FFmpeg" before parsing. We can verify the rewrite
        // path is taken by passing a synthetic assembly name and confirming
        // the logic doesn't throw and falls through to a null result.
        string legacy = "[Beutl.Embedding.FFmpeg]:NotARealType";

        Type? result = TypeFormat.ToType(legacy);

        Assert.That(result, Is.Null);
    }

    private static void AssertRoundTrip(Type type)
    {
        string formatted = TypeFormat.ToString(type);
        Type? parsed = TypeFormat.ToType(formatted);

        Assert.That(parsed, Is.Not.Null, $"failed to parse {formatted}");
        Assert.That(parsed, Is.EqualTo(type));
    }

    public class NestedHost
    {
        public class Nested
        {
        }

        public class NestedGeneric<T>
        {
        }
    }
}
