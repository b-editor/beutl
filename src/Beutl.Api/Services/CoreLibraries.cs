using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

internal static class CoreLibraries
{
    public static readonly NuGetVersion BeutlVersion = new("0.3.0");
    public static readonly PackageIdentity[] PreferredVersions =
    {
        new PackageIdentity("Beutl.Sdk", BeutlVersion),
        new PackageIdentity("Beutl.Configuration", BeutlVersion),
        new PackageIdentity("Beutl.Controls", BeutlVersion),
        new PackageIdentity("Beutl.Core", BeutlVersion),
        new PackageIdentity("Beutl.Extensibility", BeutlVersion),
        new PackageIdentity("Beutl.Engine", BeutlVersion),
        new PackageIdentity("Beutl.Language", BeutlVersion),
        new PackageIdentity("Beutl.Operators", BeutlVersion),
        new PackageIdentity("Beutl.ProjectSystem", BeutlVersion),
        new PackageIdentity("Beutl.Threading", BeutlVersion),
        new PackageIdentity("Beutl.Utilities", BeutlVersion),
    };

    public static bool IsCoreLibraries(string name)
    {
        return name is "Beutl.Sdk"
            or "Beutl.Configuration"
            or "Beutl.Controls"
            or "Beutl.Core"
            or "Beutl.Extensibility"
            or "Beutl.Engine"
            or "Beutl.Language"
            or "Beutl.Operators"
            or "Beutl.ProjectSystem"
            or "Beutl.Threading"
            or "Beutl.Utilities";
    }
}
