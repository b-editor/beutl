using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using NuGet.Frameworks;
using NuGet.Packaging;

namespace Beutl.Api.Services;

public class PackageLoader
{
    public Assembly Load(string specFile)
    {
        var framework = Helper.GetFrameworkName();
        var reader = new NuspecReader(specFile);

        var nearest = Helper.FrameworkReducer.GetNearest(framework, reader.GetDependencyGroups().Select(x => x.TargetFramework));

        string directory = Directory.GetParent(specFile)!.FullName;
        string name = Path.GetFileNameWithoutExtension(specFile);

        string mainLibrary = Path.Combine(directory, "lib", nearest.ToString(), $"{name}.dll");

        PluginLoadContext loadContext = new PluginLoadContext(specFile);
        return loadContext.LoadFromAssemblyName(new AssemblyName(name));
    }
}
