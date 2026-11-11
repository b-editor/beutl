namespace Beutl.Editor.Services.LibraryThumbnails;

/// <summary>
/// The sample picture a filter effect's thumbnail shows the effect on where the project has no content to show it on.
/// </summary>
/// <remarks>
/// Each suits what some effects change, so a <see cref="LibraryThumbnailRecipes">recipe</see> picks the one that shows
/// its effect best. Every sample fills a frame of 1920 x 1080 units, the size a recipe's values are given for.
/// </remarks>
public enum LibraryThumbnailSample
{
    /// <summary>
    /// A photograph of a landscape by day, with fine detail and colors of every kind: for blurs, colors and
    /// distortions.
    /// </summary>
    Landscape,

    /// <summary>
    /// A photograph of a city at night, small bright lights on dark: for glows, and keys on brightness.
    /// </summary>
    NightLights,

    /// <summary>A photograph of a subject in front of a green screen: for keys on a color.</summary>
    GreenScreen,

    /// <summary>
    /// An object cut out, with nothing around it: for shadows, and for effects that move or repeat what they apply to.
    /// </summary>
    Object,

    /// <summary>A word in bold letters cut out, with nothing around it: for outlines and effects on edges.</summary>
    Text,

    /// <summary>Flat shapes in bright colors with sharp edges: for edges, displacements and hues.</summary>
    Shapes,
}
