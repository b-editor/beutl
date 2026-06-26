namespace Beutl.Testing.Headless;

/// <summary>
/// Redirects <c>BEUTL_HOME</c> to a throwaway temp directory for the duration of a test assembly,
/// so config auto-save (settings.json, recent projects) never touches the developer's real ~/.beutl.
/// </summary>
public static class BeutlHomeIsolation
{
    private static string? s_previous;
    private static string? s_tempDir;

    // Must run before any GlobalConfiguration access; the directory must exist first because
    // BeutlEnvironment only honors BEUTL_HOME when the path already exists.
    public static string Begin(string prefix = "beutl-e2e")
    {
        s_previous = Environment.GetEnvironmentVariable("BEUTL_HOME");
        s_tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(s_tempDir);
        Environment.SetEnvironmentVariable("BEUTL_HOME", s_tempDir);
        return s_tempDir;
    }

    public static void End()
    {
        Environment.SetEnvironmentVariable("BEUTL_HOME", s_previous);
        try
        {
            if (s_tempDir is not null && Directory.Exists(s_tempDir))
            {
                Directory.Delete(s_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        s_tempDir = null;
        s_previous = null;
    }

    public static string? CurrentHome => s_tempDir;
}
