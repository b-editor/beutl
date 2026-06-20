using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Beutl.UnitTests.Core;

public class JsonConvertersTests
{
    private static readonly JsonSerializerOptions s_options = JsonHelper.SerializerOptions;

    [Test]
    public void CultureInfo_RoundTrip()
    {
        var ja = CultureInfo.GetCultureInfo("ja-JP");
        string json = JsonSerializer.Serialize(ja, s_options);
        Assert.That(json, Is.EqualTo("\"ja-JP\""));

        CultureInfo? back = JsonSerializer.Deserialize<CultureInfo>(json, s_options);
        Assert.That(back, Is.EqualTo(ja));
    }

    [Test]
    public void DirectoryInfo_RoundTrip()
    {
        string path = Path.GetTempPath();
        var dir = new DirectoryInfo(path);
        string json = JsonSerializer.Serialize(dir, s_options);

        DirectoryInfo? back = JsonSerializer.Deserialize<DirectoryInfo>(json, s_options);
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.FullName, Is.EqualTo(dir.FullName));
    }

    [Test]
    public void FileInfo_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "beutl-test-file.txt");
        var file = new FileInfo(path);
        string json = JsonSerializer.Serialize(file, s_options);

        FileInfo? back = JsonSerializer.Deserialize<FileInfo>(json, s_options);
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.FullName, Is.EqualTo(file.FullName));
    }

    [Test]
    public void Vector3_RoundTrip()
    {
        var v = new Vector3(1.5f, -2.5f, 3.5f);
        string json = JsonSerializer.Serialize(v, s_options);
        Assert.That(json, Is.EqualTo("\"1.5,-2.5,3.5\""));

        Vector3 back = JsonSerializer.Deserialize<Vector3>(json, s_options);
        Assert.That(back, Is.EqualTo(v));
    }

    [Test]
    public void Quaternion_RoundTrip()
    {
        var q = new Quaternion(0.1f, 0.2f, 0.3f, 1f);
        string json = JsonSerializer.Serialize(q, s_options);
        Assert.That(json, Is.EqualTo("\"0.1,0.2,0.3,1\""));

        Quaternion back = JsonSerializer.Deserialize<Quaternion>(json, s_options);
        Assert.That(back, Is.EqualTo(q));
    }

    [Test]
    public void Rational_FromString_Reads()
    {
        Rational r = JsonSerializer.Deserialize<Rational>("\"3/4\"", s_options);
        Assert.That(r, Is.EqualTo(new Rational(3, 4)));
    }

    [Test]
    public void Rational_FromObject_ReadsNumeratorDenominator()
    {
        const string json = "{\"Numerator\":7,\"Denominator\":2}";
        Rational r = JsonSerializer.Deserialize<Rational>(json, s_options);
        Assert.That(r, Is.EqualTo(new Rational(7, 2)));
    }

    [Test]
    public void Rational_Write_AsString()
    {
        var r = new Rational(5, 6);
        string json = JsonSerializer.Serialize(r, s_options);
        Assert.That(json, Is.EqualTo("\"5/6\""));
    }

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
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<Vector3>("\"not-a-vector\"", s_options));
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
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<Quaternion>("\"1,2,3\"", s_options));
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
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<Rational>("\"3:4\"", s_options));
    }

    [Test]
    public void Reference_RoundTripsViaConverter()
    {
        var reference = new Reference<JsonReferenceTestObject>(Guid.NewGuid());
        string json = JsonSerializer.Serialize(reference, s_options);
        Assert.That(json, Is.EqualTo($"\"{reference.Id}\""));

        Reference<JsonReferenceTestObject> back = JsonSerializer.Deserialize<Reference<JsonReferenceTestObject>>(json, s_options);
        Assert.That(back.Id, Is.EqualTo(reference.Id));
    }

    private sealed class JsonReferenceTestObject : CoreObject;
}
