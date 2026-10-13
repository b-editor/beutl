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

    public static string GetPackagesDirectoryPath()
    {
        return Path.Combine(GetHomeDirectoryPath(), "packages");
    }

    public static string GetSideloadsDirectoryPath()
    {
        return Path.Combine(GetHomeDirectoryPath(), "sideloads");
    }

    public static string GetTemplatesDirectoryPath()
    {
        return Path.Combine(GetHomeDirectoryPath(), "templates");
    }

    public static string GetMaterialsDirectoryPath()
    {
        return Path.Combine(GetHomeDirectoryPath(), "materials");
    }
}
