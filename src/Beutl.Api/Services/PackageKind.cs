namespace Beutl.Api.Services;

/// <summary>
/// What a store package delivers.
/// </summary>
/// <remarks>
/// The store has no column for this. A package declares its kind with a reserved tag —
/// <c>beutl-material</c> or <c>beutl-template</c> — and one that carries neither is an
/// extension. The tags are prefixed because the same vocabulary is read out of a package's
/// nuspec, where a bare "material" is an ordinary tag plenty of unrelated packages carry.
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
    Template,

    /// <summary>Ships both a <c>materials/</c> and a <c>templates/</c> payload.</summary>
    Both
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
    public const string MaterialTag = "beutl-material";

    public const string TemplateTag = "beutl-template";

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

        bool material = false;
        bool template = false;
        foreach (string tag in tags)
        {
            if (tag == MaterialTag)
            {
                material = true;
            }
            else if (tag == TemplateTag)
            {
                template = true;
            }
        }

        if (material && template)
        {
            return PackageKind.Both;
        }

        if (material)
        {
            return PackageKind.Material;
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
    /// The query vocabulary is the kind name, not the reserved tag: the server maps
    /// <c>material</c> onto the <c>beutl-material</c> tag.
    /// </summary>
    public static string ToQueryValue(this PackageKindFilter filter)
    {
        return filter switch
        {
            PackageKindFilter.Extension => "extension",
            PackageKindFilter.Material => "material",
            PackageKindFilter.Template => "template",
            _ => "all"
        };
    }
}
