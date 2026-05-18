using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Beutl.UnitTests.Core;

public class JsonConvertersExtraTests
{
    private static readonly JsonSerializerOptions s_options = JsonHelper.SerializerOptions;

    [Test]
    public void CultureInfo_InvariantCulture_RoundTrips()
    {
        string json = JsonSerializer.Serialize(CultureInfo.InvariantCulture, s_options);
        CultureInfo? back = JsonSerializer.Deserialize<CultureInfo>(json, s_options);
        Assert.That(back, Is.EqualTo(CultureInfo.InvariantCulture));
    }

    [Test]
    public void CultureInfo_NullJsonValue_ReturnsNull()
    {
        Assert.That(JsonSerializer.Deserialize<CultureInfo>("null", s_options), Is.Null);
    }

    [Test]
    public void Vector3_InvalidString_Throws()
    {
        Assert.Throws<FormatException>(() =>
            JsonSerializer.Deserialize<Vector3>("\"not-a-vector\"", s_options)
        );
    }

    [Test]
    public void Vector3_NullValue_Throws()
    {
        Exception? ex = Assert.Catch(() => JsonSerializer.Deserialize<Vector3>("null", s_options));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Vector3"));
    }

    [Test]
    public void Quaternion_TooFewComponents_Throws()
    {
        Assert.Throws<FormatException>(() =>
            JsonSerializer.Deserialize<Quaternion>("\"1,2,3\"", s_options)
        );
    }

    [Test]
    public void DirectoryInfo_RelativePath_RoundTrips()
    {
        var dir = new DirectoryInfo("relative/path");
        string json = JsonSerializer.Serialize(dir, s_options);
        DirectoryInfo? back = JsonSerializer.Deserialize<DirectoryInfo>(json, s_options);
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.FullName, Is.EqualTo(dir.FullName));
    }

    [Test]
    public void FileInfo_NullValue_ReturnsNull()
    {
        Assert.That(JsonSerializer.Deserialize<FileInfo>("null", s_options), Is.Null);
    }

    [Test]
    public void Rational_InvalidString_Throws()
    {
        Assert.Throws<FormatException>(() =>
            JsonSerializer.Deserialize<Rational>("\"3:4\"", s_options)
        );
    }

    [Test]
    public void Reference_RoundTripsViaConverter()
    {
        var reference = new Reference<JsonReferenceTestObject>(Guid.NewGuid());
        string json = JsonSerializer.Serialize(reference, s_options);
        Assert.That(json, Is.EqualTo($"\"{reference.Id}\""));

        Reference<JsonReferenceTestObject> back = JsonSerializer.Deserialize<
            Reference<JsonReferenceTestObject>
        >(json, s_options);
        Assert.That(back.Id, Is.EqualTo(reference.Id));
    }

    private sealed class JsonReferenceTestObject : CoreObject;
}
