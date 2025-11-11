namespace Beutl.IO;

public class BlobFileSource : IFileSource
{
    private byte[] _data = Array.Empty<byte>();

    public bool IsBlob => true;

    public Uri Uri
    {
        get => field ?? throw new InvalidOperationException("URI is not set.");
        private set;
    }

    public void WriteTo(Stream stream)
    {
        stream.Write(_data, 0, _data.Length);
    }

    public void ReadFrom(Stream stream, Uri uri)
    {
        using (stream)
        {
            Uri = uri;
            using MemoryStream ms = new();
            stream.CopyTo(ms);
            _data = ms.ToArray();
        }
    }
}
