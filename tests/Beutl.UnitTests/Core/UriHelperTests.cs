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
        // Constructed without going through Uri parsing of the data scheme,
        // so an invalid data URI without a comma raises FormatException.
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
