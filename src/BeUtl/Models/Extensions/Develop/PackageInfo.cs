namespace BeUtl.Models.Extensions.Develop;

public record PackageInfo(
    string DisplayName,
    string Name,
    string Description,
    string ShortDescription,
    bool IsVisible,
    ImageLink? LogoImage,
    ImageLink[] Screenshots) : IPackage;
