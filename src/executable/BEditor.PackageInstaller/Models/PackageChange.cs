using System;

namespace BEditor.PackageInstaller.Models
{
    public enum PackageChangeType
    {
        Install,
        Uninstall,
        Update,
        Cancel
    }

    public sealed record PackageChange(Guid Id, string Name, string MainAssembly, string Author, string Version, string License, PackageChangeType Type, string? Url = null);
}