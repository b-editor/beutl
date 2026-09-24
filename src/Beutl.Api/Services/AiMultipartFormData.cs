using Refit;

namespace Beutl.Api.Services;

internal static class AiMultipartFormData
{
    // The server's FormData parser requires quoted disposition parameters.
    // MultipartFormDataContent emits simple file names as bare tokens otherwise.
    public static StreamPart File(Stream stream, string fileName, string mediaType)
        => new(stream, Quote(fileName), mediaType);

    public static string Quote(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
