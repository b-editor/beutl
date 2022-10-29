using System.IO.IsolatedStorage;
using System.Text.Json;

namespace Beutl.Api.Services;

public class InstalledPackageRepository : IBeutlApiResource
{
    private readonly List<string> _packages = new();
    private const string FileName = "installedPackages.json";

    public InstalledPackageRepository()
    {
        Restore();
    }

    public IEnumerable<string> GetLocalPackages()
    {
        return _packages;
    }

    public void AddPackage(string installedPath)
    {
        if (!Directory.Exists(installedPath))
            throw new DirectoryNotFoundException();

        _packages.Add(installedPath);
        Save();
    }

    public void RemovePackage(string installedPath)
    {
        _packages.Remove(installedPath);
        Save();
    }
    
    public bool ExistsPackage(string installedPath)
    {
        return _packages.Contains(installedPath);
    }

    private void Save()
    {
        using (var storagefile = IsolatedStorageFile.GetUserStoreForAssembly())
        using (IsolatedStorageFileStream stream = storagefile.CreateFile(FileName))
        {
            JsonSerializer.Serialize(stream, _packages);
        }
    }

    private void Restore()
    {
        using (var storagefile = IsolatedStorageFile.GetUserStoreForAssembly())
        {
            if (storagefile.FileExists(FileName))
            {
                using (IsolatedStorageFileStream stream = storagefile.CreateFile(FileName))
                {
                    if(JsonSerializer.Deserialize<string[]>(stream) is string[] packages)
                    {
                        _packages.Clear();

                        _packages.AddRange(packages);
                    }
                }
            }
        }
    }
}
