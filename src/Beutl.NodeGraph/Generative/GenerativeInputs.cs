using Beutl.Language;

namespace Beutl.NodeGraph.Generative;

/// <summary>Reads the files a generation is given, for nodes and for the timeline alike.</summary>
public static class GenerativeInputs
{
    /// <summary>
    /// The largest clip read into a request: the service's source limit. Anything larger is
    /// refused before it is read, rather than loaded whole only to be refused later.
    /// </summary>
    public const long MaxVideoInputBytes = 32L * 1024 * 1024;

    /// <summary>Whether a clip at this path is of a kind a generation can take.</summary>
    public static bool IsSupportedVideoFile(string path)
        => MediaTypeOf(Path.GetExtension(path)) is not null;

    /// <summary>Reads a clip handed to a generation as input, named <paramref name="name"/> plus its extension.</summary>
    public static GenerativeFileInput ReadVideoFile(string path, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string mediaType = MediaTypeOf(extension)
            ?? throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxVideoInputBytes)
                throw new GenerativeExecutionException(Strings.AiFileTooLarge);
            byte[] content = new byte[stream.Length];
            stream.ReadExactly(content);
            return new GenerativeFileInput($"{name}{extension}", mediaType, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable, ex);
        }
    }

    private static string? MediaTypeOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        _ => null,
    };
}
