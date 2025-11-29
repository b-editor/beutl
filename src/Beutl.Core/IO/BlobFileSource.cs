using Beutl.Serialization;

namespace Beutl.IO;

public class BlobFileSource : IFileSource
{
    public Uri Uri
    {
        get => field ?? throw new InvalidOperationException("URI is not set.");
        private set;
    }

    public byte[] Data { get; private set; } = [];

    public void ReadFrom(Uri uri)
    {
        Data = UriHelper.ResolveByteArray(uri);
    }
}
