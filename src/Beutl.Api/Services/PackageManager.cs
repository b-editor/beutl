using System.Reflection;

using Beutl.Api.Objects;

using BeUtl.Framework;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public sealed class PackageManager : PackageLoader
{
    internal readonly List<LocalPackage> _loadedPackage = new();
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly BeutlApiApplication _apiApplication;

    public PackageManager(
        InstalledPackageRepository installedPackageRepository,
        BeutlApiApplication apiApplication)
    {
        ExtensionProvider = new();
        _installedPackageRepository = installedPackageRepository;
        _apiApplication = apiApplication;
    }

    public IReadOnlyList<LocalPackage> LoadedPackage => _loadedPackage;

    public ExtensionProvider ExtensionProvider { get; }

    public async Task<IReadOnlyList<LocalPackage>> GetPackages()
    {
        async Task<Package?> GetPackage(string id)
        {
            try
            {
                PackageResponse package = await _apiApplication.Packages.GetPackageAsync(id);
                ProfileResponse profile = await _apiApplication.Users.GetUserAsync(package.Owner.Name);

                return new Package(
                    profile: new Profile(profile, _apiApplication),
                    package,
                    _apiApplication);
            }
            catch
            {
                return null;
            }
        }

        IEnumerable<string> packages = _installedPackageRepository.GetLocalPackages();
        var list = new List<LocalPackage>(packages.TryGetNonEnumeratedCount(out int count) ? count : 4);

        foreach (string item in packages)
        {
            if (Directory.Exists(item))
            {
                var reader = new PackageFolderReader(item);
                NuspecReader nuspec = reader.NuspecReader;
                Package? package = await GetPackage(item);
                if (package == null)
                {
                    // Todo: リモートから取得できない場合
                }
                else
                {
                    list.Add(new LocalPackage(package)
                    {
                        Version = nuspec.GetVersion().ToString(),
                        InstalledPath = item,
                    });
                }
            }
        }

        return list;
    }

    public Assembly[] Load(LocalPackage package)
    {
        var packageId = new PackageIdentity(package.Name, NuGetVersion.Parse(package.Version));
        package.InstalledPath ??= Helper.PackagePathResolver.GetInstallPath(packageId);

        Assembly[] assemblies = Load(package.InstalledPath);

        _loadedPackage.Add(package);

        var extensions = new List<Extension>();

        foreach (var assembly in assemblies)
        {
            LoadExtensions(assembly, extensions);
        }

        ExtensionProvider._allExtensions.Add(package.LocalId, extensions.ToArray());
        
        return assemblies;
    }

    private void LoadExtensions(Assembly assembly, List<Extension> extensions)
    {
        foreach (Type type in assembly.GetExportedTypes())
        {
            if (type.GetCustomAttribute<ExportAttribute>() is { })
            {
                if (type.IsAssignableTo(typeof(Extension))
                    && Activator.CreateInstance(type) is Extension extension)
                {
                    extension.Load();

                    extensions.Add(extension);
                    ExtensionProvider.InvalidateCache();
                }
            }
        }
    }
}
