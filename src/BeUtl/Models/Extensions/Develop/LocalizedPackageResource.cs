using System.Globalization;

namespace BeUtl.Models.Extensions.Develop;

public record LocalizedPackageResource(
    string? DisplayName,
    string? Description,
    string? ShortDescription,
    ImageLink? LogoImage,
    ImageLink[] Screenshots,
    CultureInfo Culture)
    : ILocalizedPackageResource;
