namespace BeUtl.Models.Extensions.Develop;

[Flags]
public enum PackageInfoFields
{
    None = 0,
    Name = 1 << 0,
    DisplayName = 1 << 1,
    Description = 1 << 2,
    ShortDescription = 1 << 3,
    IsVisible = 1 << 4,
    LogoImage = 1 << 5,
    Screenshots = 1 << 6,
    All = Name
        | DisplayName
        | Description
        | ShortDescription
        | IsVisible
        | LogoImage
        | Screenshots,
}
