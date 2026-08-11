using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private const string MaterialsContentDirectory = "materials";

    private const string TemplatesContentDirectory = "templates";

    /// <summary>
    /// Copies the payload a material or template package ships into the directory the
    /// editor reads it from. The nupkg itself stays extracted under the install path so
    /// uninstall and cleanup keep working the same way they do for extensions.
    /// </summary>
    public void InstallDataPackage(LocalPackage package)
    {
        if (string.IsNullOrEmpty(package.InstalledPath))
        {
            throw new ArgumentException(
                $"'{package.Name}' has not been extracted yet.",
                nameof(package));
        }

        string name = ValidatePackageName(package.Name);
        bool hasMaterial = package.Tags.Contains(PackageKinds.MaterialTag);
        bool hasTemplate = package.Tags.Contains(PackageKinds.TemplateTag);
        if (!hasMaterial && !hasTemplate)
        {
            throw new ArgumentException(
                $"'{package.Name}' is an extension package and has no data payload.",
                nameof(package));
        }

        if (hasMaterial)
        {
            InstallPayload(package, name, MaterialsContentDirectory, BeutlEnvironment.GetMaterialsDirectoryPath());
        }

        if (hasTemplate)
        {
            InstallPayload(package, name, TemplatesContentDirectory, BeutlEnvironment.GetTemplatesDirectoryPath());
        }
    }

    private void InstallPayload(LocalPackage package, string name, string contentDirectory, string root)
    {
        string source = Path.Combine(package.InstalledPath!, contentDirectory);
        string destination = Path.Combine(root, name);

        // An update lands here too, and a file the new version dropped would otherwise
        // stay registered forever.
        if (!DeleteIfExists(destination))
        {
            throw new IOException($"Could not clear the existing package data directory '{destination}'.");
        }

        if (!Directory.Exists(source))
        {
            _logger.LogWarning(
                "Package {PackageName} is tagged {PackageKind} but ships no {ContentDirectory} directory.",
                package.Name, package.Tags.GetPackageKind(), contentDirectory);
            return;
        }

        CopyDirectory(source, destination);
        _logger.LogInformation(
            "Installed the {ContentDirectory} payload of {PackageName} into {Destination}.",
            contentDirectory, package.Name, destination);
    }

    /// <summary>
    /// Removes the payload directories <see cref="InstallDataPackage"/> created.
    /// Returns <see langword="false"/> when something was left behind.
    /// </summary>
    /// <remarks>
    /// Both candidates are removed without consulting the nuspec: uninstall also runs when
    /// the extracted package is already gone, and the directory a package never created
    /// simply is not there.
    /// </remarks>
    public bool UninstallDataPackage(string packageName)
    {
        string name = ValidatePackageName(packageName);
        bool templates = DeleteIfExists(Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name));
        bool materials = DeleteIfExists(Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name));
        return templates && materials;
    }

    // The name is a NuGet id read out of a downloaded nuspec, and it becomes a directory
    // this class also deletes; nothing else stops it from pointing outside the home.
    private static string ValidatePackageName(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)
            || packageName is "." or ".."
            || packageName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || packageName != Path.GetFileName(packageName))
        {
            throw new ArgumentException($"'{packageName}' is not a usable directory name.", nameof(packageName));
        }

        return packageName;
    }

    private bool DeleteIfExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete the package data directory {Directory}.", directory);
            return false;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
