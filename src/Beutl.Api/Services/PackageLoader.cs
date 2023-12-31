using System.Reflection;

using NuGet.Frameworks;
using NuGet.Packaging;

namespace Beutl.Api.Services;

public abstract class PackageLoader : IBeutlApiResource
{
#pragma warning disable CA1822
    protected Assembly[] Load(string installedPath)
#pragma warning restore CA1822
    {
        NuGetFramework framework = Helper.GetFrameworkName();
        var reader = new PackageFolderReader(installedPath);

        NuGetFramework nearest = Helper.FrameworkReducer
            .GetNearest(framework, reader.GetPackageDependencies().Select(x => x.TargetFramework))
            ?? throw new Exception("Unknown Framework");

        string name = Path.GetFileName(installedPath);

        string mainDirectory = Path.Combine(installedPath, "lib", nearest.ToString());

        var loadContext = new PluginLoadContext(mainDirectory, reader);
        string[] asmFiles = Directory.GetFiles(mainDirectory, "*.dll");
        Assembly[] assemblies = new Assembly[asmFiles.Length];

        int index = 0;
        foreach (string asmFile in asmFiles)
        {
            assemblies[index++] = loadContext.LoadFromAssemblyPath(asmFile);
        }

        return assemblies;
    }

#pragma warning disable CA1822
    protected Assembly[] SideLoad(string installedPath)
#pragma warning restore CA1822
    {
        string mainDirectory = Path.Combine(installedPath);

        var loadContext = new PluginLoadContext(mainDirectory);
        string name = Path.GetFileName(mainDirectory);
        string asmFile = Path.Combine(mainDirectory, $"{name}.dll");

        return [loadContext.LoadFromAssemblyPath(asmFile)];
    }
}
