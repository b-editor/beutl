namespace BeUtl.Models.Extensions.Develop;

public sealed record LocalizedPackageResource(
    string? DisplayName,
    string? Description,
    string? ShortDescription,
    ImageLink? LogoImage,
    ImageLink[] Screenshots,
    CultureInfo Culture)
    : ILocalizedPackageResource;
