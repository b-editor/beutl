namespace Beutl.Api.Services;

/// <summary>
/// What a store package delivers.
/// </summary>
/// <remarks>
/// The store has no column for this. A package declares its kind with a reserved tag —
/// <c>material</c> or <c>template</c> — and one that carries neither is an extension.
/// Named "kind" rather than "type" because <c>NuGet.Packaging.Core.PackageType</c> is in
/// scope across the install pipeline.
/// </remarks>
public enum PackageKind
{
    /// <summary>Ships assemblies that are loaded into the editor.</summary>
    Extension,

    /// <summary>Ships media files (images, audio, fonts) under <c>materials/</c>.</summary>
    Material,

    /// <summary>Ships object templates under <c>templates/</c>.</summary>
    Template
}

/// <summary>
/// A store listing filter: <see cref="PackageKind"/> plus "every kind".
/// </summary>
public enum PackageKindFilter
{
    All,
    Extension,
    Material,
    Template
}

public static class PackageKinds
{
    public const string MaterialTag = "material";

    public const string TemplateTag = "template";

    public static IReadOnlyList<string> ReservedTags { get; } = [MaterialTag, TemplateTag];

    /// <remarks>
    /// Matching is case-sensitive on purpose: the server filters with a case-sensitive
    /// array predicate, so a looser rule here would classify packages the store's own
    /// listing does not.
    /// </remarks>
    public static bool IsReservedTag(string tag)
    {
        return tag is MaterialTag or TemplateTag;
    }

    public static PackageKind GetPackageKind(this IEnumerable<string>? tags)
    {
        if (tags == null)
        {
            return PackageKind.Extension;
        }

        bool template = false;
        foreach (string tag in tags)
        {
            // Material wins over template so a package carrying both still lands in the
            // single bucket the server's filter puts it in.
            if (tag == MaterialTag)
            {
                return PackageKind.Material;
            }

            if (tag == TemplateTag)
            {
                template = true;
            }
        }

        return template ? PackageKind.Template : PackageKind.Extension;
    }

    /// <summary>
    /// The tags the package author actually chose, with the kind markers taken out.
    /// </summary>
    public static IEnumerable<string> VisibleTags(this IEnumerable<string> tags)
    {
        return tags.Where(x => !IsReservedTag(x));
    }

    /// <summary>
    /// The wire value the discover endpoints expect for the <c>type</c> query parameter.
    /// </summary>
    public static string ToQueryValue(this PackageKindFilter filter)
    {
        return filter switch
        {
            PackageKindFilter.Extension => "extension",
            PackageKindFilter.Material => MaterialTag,
            PackageKindFilter.Template => TemplateTag,
            _ => "all"
        };
    }
}
