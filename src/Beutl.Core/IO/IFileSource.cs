using System.Text.Json.Serialization;

namespace Beutl.IO;

[JsonConverter(typeof(FileSourceJsonConverter))]
public interface IFileSource
{
    bool IsBlob { get; }

    Uri Uri { get; }

    void WriteTo(Stream stream);

    void ReadFrom(Stream stream, Uri uri);
}
