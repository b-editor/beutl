using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;

using BeUtl.Configuration;

namespace BeUtl.Framework;

public sealed class PackageManager
{
    public static readonly PackageManager Instance = new();
    private readonly List<Package> _loadedPackage = new();
    private ResourceDictionary _resourceDictionary = new();
    private Application? _application;

    private PackageManager()
    {
        GlobalConfiguration.Instance.ViewConfig.GetObservable(ViewConfig.UICultureProperty)
            .Subscribe(OnUICultureChanged);
    }

    public IReadOnlyList<Package> LoadedPackage => _loadedPackage;

    public string BaseDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "extensions");

    public IEnumerable<PackageInfo> GetPackageInfos()
    {
        return Directory.EnumerateDirectories(BaseDirectory)
            .Select(d => (packageFile: Path.Combine(d, "package.json"), directory: d))
            .Where(t => File.Exists(t.packageFile))
            .Select(t =>
            {
                using var stream = new FileStream(t.packageFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                PackageInfo? info = JsonSerializer.Deserialize<PackageInfo>(stream);
                if (info != null)
                    info.BasePath = t.directory;
                return info;
            })
            .Where(i => i != null)!;
    }

    public void LoadPackages(IEnumerable<PackageInfo> packageInfos)
    {
        foreach (PackageInfo packageInfo in packageInfos)
        {
            string asmFile = Path.GetFullPath(packageInfo.Assembly, packageInfo.BasePath!);
            Assembly asm = Assembly.LoadFrom(asmFile);

            if (Attribute.GetCustomAttribute(asm, typeof(PackageAwareAttribute)) is PackageAwareAttribute att &&
                Activator.CreateInstance(att.PackageType) is Package package)
            {
                package.Info = packageInfo;
                _loadedPackage.Add(package);

                foreach (Extension extension in package.GetExtensions())
                {
                    extension.Load();
                }
            }
        }

        LoadResources(CultureInfo.CurrentUICulture);
    }

    public void AttachToApplication(Application app)
    {
        _application = app;
        app.Resources.MergedDictionaries.Add(_resourceDictionary);
    }

    public void DetachFromApplication(Application app)
    {
        _application = null;
        app.Resources.MergedDictionaries.Remove(_resourceDictionary);
    }

    private void OnUICultureChanged(CultureInfo obj)
    {
        LoadResources(obj);
    }

    private void LoadResources(CultureInfo ci)
    {
        var newResDictionary = new ResourceDictionary();
        foreach (Package item in CollectionsMarshal.AsSpan(_loadedPackage))
        {
            Avalonia.Controls.IResourceProvider? resource = item.GetResource(ci);
            if (resource != null)
            {
                newResDictionary.MergedDictionaries.Add(resource);
            }
        }

        if (_application != null)
        {
            _application.Resources.MergedDictionaries.Add(newResDictionary);
            _application.Resources.MergedDictionaries.Remove(_resourceDictionary);
        }

        _resourceDictionary = newResDictionary;
    }
}
