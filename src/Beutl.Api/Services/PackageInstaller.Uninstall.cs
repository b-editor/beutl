using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public partial class PackageInstaller
{
    public async Task<PackageUninstallContext> PrepareForUninstall(
        string installedPath,
        bool clean = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            PackageIdentity uninstallPackage = new PackageFolderReader(installedPath).GetIdentity();
            PackageIdentity[] unnecessaryPackages = { uninstallPackage };

            if (clean)
            {
                PackageIdentity[] installedPackages = _installedPackageRepository.GetLocalPackages()
                    .Except(unnecessaryPackages)
                    .ToArray();

                unnecessaryPackages = UnnecessaryPackages(installedPackages);
            }

            long size = 0;
            foreach (PackageIdentity package in unnecessaryPackages)
            {
                string directory = Helper.PackagePathResolver.GetInstalledPath(package);
                foreach (string file in Directory.GetFiles(directory))
                {
                    size += new FileInfo(file).Length;
                }
            }

            return new PackageUninstallContext(uninstallPackage, installedPath)
            {
                UnnecessaryPackages = unnecessaryPackages,
                SizeToBeReleased = size
            };
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }

    public async Task Uninstall(
        PackageUninstallContext context,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitAny(s_mutex, cancellationToken.WaitHandle);
            cancellationToken.ThrowIfCancellationRequested();

            var failedPackages = new List<string>();
            long totalSize = 0;
            foreach (PackageIdentity package in context.UnnecessaryPackages)
            {
                string directory = Helper.PackagePathResolver.GetInstalledPath(package);
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

                _installedPackageRepository.RemovePackage(package);
            }

            context.FailedPackages = failedPackages;
        }
        finally
        {
            s_mutex.ReleaseMutex();
        }
    }
}
