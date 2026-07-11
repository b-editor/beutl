namespace Beutl.Media.Decoding;

// Thrown when a media file is present but no registered decoder can open it, distinguishing an
// unsupported/undecodable file from a genuinely missing one (FileNotFoundException).
public sealed class UnsupportedMediaException(string message, string? fileName) : Exception(message)
{
    public string? FileName { get; } = fileName;
}
