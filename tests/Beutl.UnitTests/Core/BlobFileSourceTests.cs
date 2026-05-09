using System.Text;
using Beutl.IO;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class BlobFileSourceTests
{
    [Test]
    public void Default_DataIsEmpty()
    {
        var source = new BlobFileSource();

        Assert.That(source.Data, Is.Empty);
    }

    [Test]
    public void Uri_BeforeReadFrom_Throws()
    {
        var source = new BlobFileSource();

        Assert.That(() => _ = source.Uri, Throws.InvalidOperationException);
    }

    [Test]
    public void ReadFrom_DataUri_PopulatesBytes()
    {
        byte[] payload = Encoding.UTF8.GetBytes("blob");
        Uri uri = UriHelper.CreateBase64DataUri("application/octet-stream", payload);
        var source = new BlobFileSource();

        source.ReadFrom(uri);

        Assert.That(source.Data, Is.EqualTo(payload));
    }

    [Test]
    public void ReadFrom_FileUri_ReadsContent()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("file-blob");
            File.WriteAllBytes(path, payload);
            var source = new BlobFileSource();

            source.ReadFrom(new Uri(path));

            Assert.That(source.Data, Is.EqualTo(payload));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
