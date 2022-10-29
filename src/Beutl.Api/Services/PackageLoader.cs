using System.Reflection;

using NuGet.Frameworks;
using NuGet.Packaging;

namespace Beutl.Api.Services;

public abstract class PackageLoader : IBeutlApiResource
{
#pragma warning disable CA1822
    protected Assembly Load(string installedPath)
#pragma warning restore CA1822
    {
        NuGetFramework framework = Helper.GetFrameworkName();
        var reader = new PackageFolderReader(installedPath);

        NuGetFramework nearest = Helper.FrameworkReducer.GetNearest(framework, reader.GetPackageDependencies().Select(x => x.TargetFramework));

        string name = Path.GetFileNameWithoutExtension(installedPath);

        string mainDirectory = Path.Combine(installedPath, "lib", nearest.ToString());

        var loadContext = new PluginLoadContext(mainDirectory);
        return loadContext.LoadFromAssemblyName(new AssemblyName(name));
    }
}
