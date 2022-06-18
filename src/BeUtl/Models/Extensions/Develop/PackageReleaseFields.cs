namespace BeUtl.Models.Extensions.Develop;

[Flags]
public enum PackageReleaseFields
{
    None = 0,
    Version = 1 << 0,
    Title = 1 << 1,
    Body = 1 << 2,
    IsVisible = 1 << 3,
    DownloadLink = 1 << 4,
    SHA256 = 1 << 5,
    All = Version
        | Title
        | Body
        | IsVisible
        | DownloadLink
        | SHA256,
}
