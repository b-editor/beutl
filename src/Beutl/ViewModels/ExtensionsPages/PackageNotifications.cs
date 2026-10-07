using Beutl.Services;

namespace Beutl.ViewModels.ExtensionsPages;

// The notifications the package views show when an operation finishes or is deferred to the next launch.
internal static class PackageNotifications
{
    public static void Installed(string packageName)
    {
        NotificationService.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_Installed, packageName));
    }

    public static void ScheduledInstallation(string packageName)
    {
        NotificationService.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledInstallation, packageName));
    }

    public static void Updated(string packageName)
    {
        NotificationService.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_Updated, packageName));
    }

    public static void ScheduledUpdate(string packageName)
    {
        NotificationService.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUpdate, packageName));
    }

    public static void Uninstalled(string packageName)
    {
        NotificationService.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_Uninstalled, packageName));
    }

    public static void ScheduledUninstallation(string packageName)
    {
        NotificationService.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUninstallation, packageName));
    }
}
