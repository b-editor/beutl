using System.Text.Json.Serialization;

namespace Beutl.IO;

[JsonConverter(typeof(FileSourceJsonConverter))]
public interface IFileSource
{
    Uri Uri { get; }

    void ReadFrom(Uri uri);
}
