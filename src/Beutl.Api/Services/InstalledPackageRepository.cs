using System.IO.IsolatedStorage;
using System.Text.Json;

namespace Beutl.Api.Services;

public class InstalledPackageRepository
{
    private readonly List<string> _packages = new();
    private const string FileName = "installedPackages.json";

    private InstalledPackageRepository()
    {
        Restore();
    }

    public IEnumerable<string> GetLocalPackages()
    {
        return _packages;
    }

    public void AddPackage(string specFile)
    {
        if (!Directory.Exists(specFile))
            throw new DirectoryNotFoundException();

        _packages.Add(specFile);
        Save();
    }

    public void RemovePackage(string specFile)
    {
        _packages.Remove(specFile);
        Save();
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
