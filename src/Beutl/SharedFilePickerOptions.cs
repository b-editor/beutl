using Avalonia.Platform.Storage;

namespace Beutl;

public static class SharedFilePickerOptions
{
    public static readonly FilePickerFileType NuGetPackageFileType = new("NuGet Package File")
    {
        MimeTypes = new string[] { "application/x-beutl-package" },
        Patterns = new string[] { "*.nupkg" }
    };
    public static readonly FilePickerFileType NuGetPackageManifestFileType = new("NuGet Package Manifest")
    {
        MimeTypes = new string[] { "application/xml" },
        Patterns = new string[] { "*.nuspec" }
    };
    public static readonly FilePickerOpenOptions NuGetPackage = new()
    {
        FileTypeFilter = new[] { NuGetPackageFileType }
    };

    public static FilePickerOpenOptions OpenImage()
    {
        return new()
        {
            FileTypeFilter = new FilePickerFileType[]
            {
                new FilePickerFileType("All Images")
                {
                    Patterns = new string[]
                    {
                        // SKEncodedImageFormat
                        "*.bmp",
                        "*.gif",
                        "*.ico",
                        "*.jpg",
                        "*.jpeg",
                        "*.png",
                        "*.wbmp",
                        "*.webp",
                        "*.pkm",
                        "*.ktx",
                        "*.astc",
                        "*.dng",
                        "*.heif",
                        "*.avif",
                    },
                    AppleUniformTypeIdentifiers = new[] { "public.image" },
                    MimeTypes = new[] { "image/*" }
                }
            }
        };
    }

    public static FilePickerSaveOptions SaveImage()
    {
        return new()
        {
            FileTypeChoices = new FilePickerFileType[]
            {
                new FilePickerFileType("All Images")
                {
                    Patterns = new string[]
                    {
                        // SKEncodedImageFormat
                        "*.bmp",
                        "*.gif",
                        "*.ico",
                        "*.jpg",
                        "*.jpeg",
                        "*.png",
                        "*.wbmp",
                        "*.webp",
                        "*.pkm",
                        "*.ktx",
                        "*.astc",
                        "*.dng",
                        "*.heif",
                        "*.avif",
                    },
                    AppleUniformTypeIdentifiers = new[] { "public.image" },
                    MimeTypes = new[] { "image/*" }
                }
            }
        };
    }
}
