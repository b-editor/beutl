using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    private string[] UnnecessaryPackages(IEnumerable<string>? installedPackages = null)
    {
        if (!Directory.Exists(Helper.InstallPath))
        {
            return Array.Empty<string>();
        }

        NuGetFramework framework = Helper.GetFrameworkName();

        var availablePackages = new HashSet<PackageDependencyInfo>(PackageIdentityComparer.Default);

        installedPackages ??= _installedPackageRepository.GetLocalPackages();

        foreach (string item in installedPackages)
        {
            var reader = new PackageFolderReader(item);
            PackageIdentity packageId = reader.GetIdentity();

            IEnumerable<PackageDependencyGroup> deps = reader.GetPackageDependencies();
            NuGetFramework nearest = Helper.FrameworkReducer.GetNearest(
                framework,
                deps.Select(x => x.TargetFramework));

            Helper.GetPackageDependencies(
                new PackageDependencyInfo(packageId, deps
                    .Where(x => x.TargetFramework == nearest)
                    .SelectMany(x => x.Packages)),
                framework,
                availablePackages);
        }

        string[] directories = Directory.GetDirectories(Helper.InstallPath);

        return directories.ExceptBy(
            availablePackages.Select(x => $"{x.Id}.{x.Version}"),
            y => Path.GetFileName(y))
            .ToArray();
    }

    public async Task<PackageCleanContext> PrepareForClean(CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            string[] unnecessaryPackages = UnnecessaryPackages();

            long size = 0;
            foreach (string directory in unnecessaryPackages)
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    size += new FileInfo(file).Length;
                }
            }

            return new PackageCleanContext(unnecessaryPackages, size);
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }

    public async Task Clean(
        PackageCleanContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            var failedPackages = new List<string>();
            long totalSize = 0;
            foreach (string directory in context.UnnecessaryPackages)
            {
                bool hasAnyFailtures = false;
                foreach (string file in Directory.GetFiles(directory))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalSize += fi.Length;
                        fi.Delete();
                    }
                    catch
                    {
                        hasAnyFailtures = true;
                    }

                    progress.Report(totalSize / (double)context.SizeToBeReleased);
                }

                if (hasAnyFailtures)
                {
                    failedPackages.Add(directory);
                }

                _installedPackageRepository.RemovePackage(directory);
            }

            context.FailedPackages = failedPackages;
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }
}
