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

            string[] unnecessaryPackages = new string[] { installedPath };

            if (clean)
            {
                string[] installedPackages = _installedPackageRepository.GetLocalPackages()
                    .ExceptBy(unnecessaryPackages, x => Path.GetFileName(x))
                    .ToArray();

                unnecessaryPackages = UnnecessaryPackages(installedPackages);
            }

            long size = 0;
            foreach (string directory in unnecessaryPackages)
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    size += new FileInfo(file).Length;
                }
            }

            return new PackageUninstallContext(installedPath)
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
