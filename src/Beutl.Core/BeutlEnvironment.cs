#if !Beutl_ExceptionHandler
namespace Beutl;
#else
namespace Beutl.ExceptionHandler;
#endif

public static class BeutlEnvironment
{
    public const string HomeVariable = "BEUTL_HOME";

    public static string GetHomeDirectoryPath()
    {
        var dir = Environment.GetEnvironmentVariable(HomeVariable);
        if (Directory.Exists(dir))
            return dir;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl");
    }

    // $BEUTL_HOME/packages
    public static string GetPackagesDirectoryPath()
    {
        return Path.Combine(GetHomeDirectoryPath(), "packages");
    }

    // $BEUTL_HOME/sideloads
    public static string GetSideloadsDirectoryPath()
    {
        return Path.Combine(GetHomeDirectoryPath(), "sideloads");
    }
}
