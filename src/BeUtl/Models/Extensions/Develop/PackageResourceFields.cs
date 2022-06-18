namespace BeUtl.Models.Extensions.Develop;

[Flags]
public enum PackageResourceFields
{
    None = 0,
    DisplayName = 1 << 0,
    Description = 1 << 1,
    ShortDescription = 1 << 2,
    LogoImage = 1 << 3,
    Screenshots = 1 << 4,
    Culture = 1 << 5,
    All = DisplayName | Description | ShortDescription | LogoImage | Screenshots | Culture,
}
