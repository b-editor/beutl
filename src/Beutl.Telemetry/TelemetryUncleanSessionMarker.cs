namespace Beutl.Services;

/// <summary>
/// Stores only the fact that the previous desktop process did not reach its
/// normal shutdown path. This marker deliberately has no payload: exception
/// text, paths, stack traces, and identifiers belong exclusively in the local
/// diagnostic log stream.
/// </summary>
internal static class TelemetryUncleanSessionMarker
{
    internal const string FileName = "telemetry-unclean-session.marker";

    internal static string GetPath(string? homeDirectory = null)
    {
        return Path.Combine(homeDirectory ?? BeutlEnvironment.GetHomeDirectoryPath(), FileName);
    }

    internal static bool Exists(string? homeDirectory = null)
    {
        return File.Exists(GetPath(homeDirectory));
    }

    internal static void Mark(string? homeDirectory = null)
    {
        string path = GetPath(homeDirectory);
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            using (new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough))
            {
                // A zero-byte file is the complete marker state.
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The marker is advisory and must never affect crash handling.
            }
        }
    }

    internal static void Clear(string? homeDirectory = null)
    {
        try
        {
            string path = GetPath(homeDirectory);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale advisory marker must not turn normal shutdown into a failure.
        }
    }
}
