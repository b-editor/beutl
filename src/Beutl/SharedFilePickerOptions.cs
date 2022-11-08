using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Platform.Storage;

namespace Beutl;
public static class SharedFilePickerOptions
{
    public static readonly FilePickerFileType NuGetPackageFileType = new FilePickerFileType("NuGet Package File")
    {
        MimeTypes = new string[] { "application/x-beutl-package" },
        Patterns = new string[] { "*.nupkg" }
    };
    public static readonly FilePickerFileType NuGetPackageManifestFileType = new FilePickerFileType("NuGet Package Manifest")
    {
        MimeTypes = new string[] { "application/xml" },
        Patterns = new string[] { "*.nuspec" }
    };
    public static readonly FilePickerOpenOptions NuGetPackage = new()
    {
        FileTypeFilter = new[] { NuGetPackageFileType }
    };
}
