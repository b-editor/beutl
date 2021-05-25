using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.PackageInstaller.Models
{
    public enum PackageChangeType
    {
        Install,
        Uninstall,
        Update,
        Cancel
    }

    public record PackageChange(Guid Id, string Name, string MainAssembly, string Author, string Version, string License, PackageChangeType Type, string? Url = null);
}