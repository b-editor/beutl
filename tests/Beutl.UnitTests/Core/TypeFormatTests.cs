using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Encoding;

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
        string formatted = TypeFormat.ToString(typeof(int));

        Assert.That(formatted, Does.StartWith("["));
        Assert.That(formatted, Does.Contain("]"));
        Assert.That(formatted, Does.Contain(":Int32"));
    }

    [Test]
    public void ToType_UnknownAssembly_ReturnsNull()
    {
        string formatted = "[NoSuchAssembly.Foo]:NoSuchType";

        Type? result = TypeFormat.ToType(formatted);

        Assert.That(result, Is.Null);
    }

    // Beutl 1.x built FFmpeg in as Beutl.Embedding.FFmpeg, so its settings and output profiles name the decoder and
    // the encoder that way. The two types give those names as their former names.
    [Test]
    public void ToType_FFmpegNamesOfVersion1_ResolveThroughTheirFormerNames()
    {
        // Loads the FFmpeg extension's assembly, as the app does before it reads these names.
        Type decoder = typeof(FFmpegDecoderInfo);
        Type encoder = typeof(FFmpegControlledEncodingExtension);

        Assert.Multiple(() =>
        {
            Assert.That(TypeFormat.ToType("[Beutl.Embedding.FFmpeg].Decoding:FFmpegDecoderInfo"), Is.EqualTo(decoder));
            Assert.That(
                TypeFormat.ToType("[Beutl.Embedding.FFmpeg].Encoding:FFmpegControlledEncodingExtension"),
                Is.EqualTo(encoder));
            // A name in that assembly that no type gives as a former name does not resolve.
            Assert.That(TypeFormat.ToType("[Beutl.Embedding.FFmpeg]:NotARealType"), Is.Null);
        });
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
