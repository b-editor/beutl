using Avalonia.Platform.Storage;

namespace Beutl;

public static class SharedFilePickerOptions
{
    public static readonly FilePickerFileType NuGetPackageFileType = new("NuGet Package File")
    {
        MimeTypes = ["application/x-beutl-package"],
        Patterns = ["*.nupkg"]
    };
    public static readonly FilePickerFileType NuGetPackageManifestFileType = new("NuGet Package Manifest")
    {
        MimeTypes = ["application/xml"],
        Patterns = ["*.nuspec"]
    };
    public static readonly FilePickerOpenOptions NuGetPackage = new()
    {
        FileTypeFilter = [NuGetPackageFileType]
    };

    public static FilePickerOpenOptions OpenImage()
    {
        return new()
        {
            FileTypeFilter =
            [
                new FilePickerFileType("All Images")
                {
                    Patterns =
                    [
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
                        "*.avif"
                    ],
                    AppleUniformTypeIdentifiers = ["public.image"],
                    MimeTypes = ["image/*"]
                }
            ]
        };
    }

    public static FilePickerSaveOptions SaveImage()
    {
        return new()
        {
            FileTypeChoices =
            [
                new FilePickerFileType("All Images")
                {
                    Patterns =
                    [
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
                        "*.avif"
                    ],
                    AppleUniformTypeIdentifiers = ["public.image"],
                    MimeTypes = ["image/*"]
                }
            ]
        };
    }
}
