using System.Text;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class UriHelperTests
{
    [Test]
    public void ParseDataUri_PlainText_DecodesAsUtf8()
    {
        var uri = new Uri("data:text/plain,Hello%20World");

        var (data, metadata) = UriHelper.ParseDataUri(uri);

        Assert.That(Encoding.UTF8.GetString(data), Is.EqualTo("Hello World"));
        Assert.That(metadata, Is.EqualTo("text/plain"));
    }

    [Test]
    public void ParseDataUri_Base64_DecodesBytes()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        string base64 = Convert.ToBase64String(payload);
        var uri = new Uri($"data:application/octet-stream;base64,{base64}");

        var (data, metadata) = UriHelper.ParseDataUri(uri);

        Assert.That(data, Is.EqualTo(payload));
        Assert.That(metadata, Is.EqualTo("application/octet-stream;base64"));
    }

    [Test]
    public void ParseDataUri_MissingComma_Throws()
    {
        // A data URI without a comma raises FormatException.
        var uri = new Uri("data:text/plain", UriKind.Absolute);

        Assert.That(() => UriHelper.ParseDataUri(uri), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void CreateBase64DataUri_RoundTrip()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Beutl");

        Uri uri = UriHelper.CreateBase64DataUri("text/plain", payload);

        var (data, metadata) = UriHelper.ParseDataUri(uri);
        Assert.That(uri.Scheme, Is.EqualTo("data"));
        Assert.That(metadata.EndsWith(";base64"), Is.True);
        Assert.That(data, Is.EqualTo(payload));
    }

    // A path becomes a file: URI whose LocalPath is that path, "%XX" included (#3407). new Uri(path) drops a trailing space.
    [TestCase("a%41.png")]
    [TestCase("ア50%20off.png")]
    [TestCase("50%20off.png")]
    [TestCase("100%.png")]
    [TestCase("a#b.png")]
    [TestCase("a?b.png")]
    [TestCase("two words.png")]
    [TestCase("シーン.png")]
    [TestCase("ends with a space ")]
    public void CreateFromPath_KeepsEveryCharacterOfThePath(string name)
    {
        if (name.Contains('?') && OperatingSystem.IsWindows())
            Assert.Ignore("A Windows file name cannot hold '?'.");

        string path = Path.Combine(Path.GetTempPath(), "beutl uri", name);

        Assert.That(UriHelper.CreateFromPath(path).LocalPath, Is.EqualTo(path));
    }

    // For a name without '%' the URI is new Uri(path)'s, so saved references and Uri comparisons stay as they were.
    [TestCaseSource(nameof(NamesWithoutPercent))]
    [TestCase(@"a\b.png")]
    public void CreateFromPath_GivesTheUriOfNewUri_ForAUnixPathWithoutPercent(string name)
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("new Uri(path) reads a Unix path only on Linux and macOS.");

        AssertSameAsNewUri($"/tmp/beutl uri/{name}", windows: false);
    }

    // The drive and UNC forms are string logic, checked on every OS against new Uri(path), which reads them everywhere.
    [TestCaseSource(nameof(NamesWithoutPercent))]
    public void CreateFromPath_GivesTheUriOfNewUri_ForAWindowsPathWithoutPercent(string name)
    {
        AssertSameAsNewUri($@"C:\beutl uri\{name}", windows: true);
        AssertSameAsNewUri($"c:/beutl uri/{name}", windows: true);
        AssertSameAsNewUri($@"\\server\share\beutl uri\{name}", windows: true);
    }

    [TestCase(@"C:\beutl uri\a%41.png")]
    [TestCase(@"C:\beutl uri\ア50%20off.png")]
    [TestCase(@"C:\beutl uri\%2E%2E\100%.png")]
    [TestCase(@"\\server\share\a%41.png")]
    [TestCase(@"\\server\share\ア50%20off.png")]
    public void CreateFromPath_KeepsPercentInAWindowsPath(string path)
    {
        Uri uri = UriHelper.CreateFromPath(path, windows: true);

        Assert.Multiple(() =>
        {
            Assert.That(uri.LocalPath, Is.EqualTo(path));
            Assert.That(uri.IsUnc, Is.EqualTo(path.StartsWith(@"\\", StringComparison.Ordinal)));
        });
    }

    private static IEnumerable<string> NamesWithoutPercent()
    {
        for (char c = ' '; c <= '~'; c++)
        {
            if (c is not ('%' or '/' or '\\'))
                yield return $"a{c}b.png";
        }

        yield return "photo (1).png";
        yield return "シーン.png";
        yield return "が".Normalize(NormalizationForm.FormD) + ".png";
        yield return "😀.png";
    }

    private static void AssertSameAsNewUri(string path, bool windows)
    {
        var expected = new Uri(path);
        Uri actual = UriHelper.CreateFromPath(path, windows);

        Assert.Multiple(() =>
        {
            Assert.That(actual.AbsoluteUri, Is.EqualTo(expected.AbsoluteUri), path);
            Assert.That(actual.LocalPath, Is.EqualTo(expected.LocalPath), path);
            Assert.That(actual.Host, Is.EqualTo(expected.Host), path);
        });
    }

    [Test]
    public void ResolveByteArray_File_ReturnsContent()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("payload");
            File.WriteAllBytes(path, payload);
            var uri = new Uri(path);

            byte[] result = UriHelper.ResolveByteArray(uri);

            Assert.That(result, Is.EqualTo(payload));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ResolveByteArray_DataUri_ReturnsContent()
    {
        byte[] payload = [9, 8, 7];
        Uri uri = UriHelper.CreateBase64DataUri("application/octet-stream", payload);

        byte[] result = UriHelper.ResolveByteArray(uri);

        Assert.That(result, Is.EqualTo(payload));
    }

    [Test]
    public void ResolveByteArray_UnsupportedScheme_Throws()
    {
        var uri = new Uri("https://example.com/foo");

        Assert.That(() => UriHelper.ResolveByteArray(uri), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void ResolveStream_DataUri_ReturnsReadableStream()
    {
        byte[] payload = [1, 2, 3];
        Uri uri = UriHelper.CreateBase64DataUri("application/octet-stream", payload);

        using Stream stream = UriHelper.ResolveStream(uri);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        Assert.That(ms.ToArray(), Is.EqualTo(payload));
    }

    [Test]
    public void ResolveStream_File_ReturnsReadableStream()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("hello-stream");
            File.WriteAllBytes(path, payload);
            var uri = new Uri(path);

            using Stream stream = UriHelper.ResolveStream(uri);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            Assert.That(ms.ToArray(), Is.EqualTo(payload));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ResolveStream_UnsupportedScheme_Throws()
    {
        var uri = new Uri("ftp://example.com/foo");

        Assert.That(() => UriHelper.ResolveStream(uri), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void CreateFromPath_BuildsAbsoluteUri()
    {
        string path = Path.GetTempFileName();
        try
        {
            Uri uri = UriHelper.CreateFromPath(path);
            Assert.That(uri.IsFile, Is.True);
            Assert.That(uri.LocalPath, Is.EqualTo(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
