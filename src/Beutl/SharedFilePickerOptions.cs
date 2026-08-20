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

    /// <summary>
    /// The pictures an AI endpoint takes as input. Deliberately narrower than
    /// <see cref="OpenImage"/>: the server decodes a fixed set and refuses
    /// everything else, so a format it cannot read is better left out of the
    /// picker than refused after the upload has gone out.
    /// </summary>
    public static FilePickerOpenOptions OpenAiInputImage()
        => AiImagePicker("PNG, JPEG, WebP, GIF", ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"]);

    /// <summary>
    /// The pictures a video generation takes as a first or last frame. The
    /// endpoint reads one fewer format than the image endpoints do.
    /// </summary>
    public static FilePickerOpenOptions OpenAiVideoFrame()
        => AiImagePicker("PNG, JPEG, WebP", ["*.png", "*.jpg", "*.jpeg", "*.webp"]);

    private static FilePickerOpenOptions AiImagePicker(string name, string[] patterns)
    {
        string[] mimeTypes = patterns
            .Select(pattern => pattern switch
            {
                "*.png" => "image/png",
                "*.jpg" or "*.jpeg" => "image/jpeg",
                "*.webp" => "image/webp",
                _ => "image/gif",
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new()
        {
            FileTypeFilter =
            [
                new FilePickerFileType(name)
                {
                    Patterns = patterns,
                    MimeTypes = mimeTypes,
                    AppleUniformTypeIdentifiers = mimeTypes
                        .Select(mimeType => mimeType switch
                        {
                            "image/png" => "public.png",
                            "image/jpeg" => "public.jpeg",
                            "image/webp" => "org.webmproject.webp",
                            _ => "com.compuserve.gif",
                        })
                        .ToArray(),
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

    public static FilePickerSaveOptions SaveVideo()
    {
        return new()
        {
            FileTypeChoices =
            [
                new FilePickerFileType("All Videos")
                {
                    Patterns =
                    [
                        "*.mp4",
                        "*.mov",
                        "*.webm",
                        "*.mkv"
                    ],
                    AppleUniformTypeIdentifiers = ["public.movie"],
                    MimeTypes = ["video/*"]
                }
            ]
        };
    }
}
