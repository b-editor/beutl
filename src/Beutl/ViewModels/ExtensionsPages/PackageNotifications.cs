using Beutl.Services;

namespace Beutl.ViewModels.ExtensionsPages;

// The notifications the package views show when an operation finishes or is deferred to the next launch.
internal static class PackageNotifications
{
    public static void Installed(INotificationService notifications, string packageName)
    {
        notifications.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_Installed, packageName));
    }

    public static void ScheduledInstallation(INotificationService notifications, string packageName)
    {
        notifications.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledInstallation, packageName));
    }

    public static void Updated(INotificationService notifications, string packageName)
    {
        notifications.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_Updated, packageName));
    }

    public static void ScheduledUpdate(INotificationService notifications, string packageName)
    {
        notifications.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUpdate, packageName));
    }

    public static void Uninstalled(INotificationService notifications, string packageName)
    {
        notifications.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_Uninstalled, packageName));
    }

    public static void ScheduledUninstallation(INotificationService notifications, string packageName)
    {
        notifications.ShowInformation(
            title: ExtensionsStrings.PackageInstaller,
            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUninstallation, packageName));
    }
}
