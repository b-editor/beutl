namespace Beutl.UnitTests.TestInfrastructure;

public static class SymbolicLinks
{
    // Ignores the test where this environment cannot create the link, for example without the
    // privilege on Windows (error 1314, an IOException).
    public static void CreateOrIgnore(string link, string target, bool directory = false)
    {
        try
        {
            if (directory)
                Directory.CreateSymbolicLink(link, target);
            else
                File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable: {ex.Message}");
        }
    }
}
