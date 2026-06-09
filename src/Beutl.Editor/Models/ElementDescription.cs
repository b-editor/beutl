using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Editor.Models;

/// <summary>
/// Describes how to create and place a new <c>Element</c> on the timeline.
/// </summary>
/// <param name="EngineObjectFactory">
/// Produces the initial engine object hosted by the element. The factory both constructs and
/// configures the object, so callers can supply a fully-typed object (e.g. an adjustment layer)
/// without a separate post-construction configuration step. When <paramref name="FileName"/> is
/// also set, file import takes precedence and the factory is ignored.
/// </param>
public record struct ElementDescription(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    string Name = "",
    Func<EngineObject>? EngineObjectFactory = null,
    string? FileName = null,
    Point Position = default)
{
    /// <summary>
    /// Resolves the element name: the explicit <see cref="Name"/> when set, otherwise the
    /// localized display name of <paramref name="fallbackType"/>.
    /// </summary>
    public readonly string ResolveName(Type fallbackType) =>
        string.IsNullOrEmpty(Name) ? TypeDisplayHelpers.GetLocalizedName(fallbackType) : Name;
}
