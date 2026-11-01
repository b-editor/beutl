using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Extensibility;

/// <summary>
/// Identifies the single source used to create an element request.
/// </summary>
public abstract record ElementSource
{
    protected ElementSource()
    {
    }

    /// <summary>
    /// Imports an image, video, or audio file.
    /// </summary>
    public sealed record File : ElementSource
    {
        public File(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            FileName = fileName;
        }

        public string FileName { get; }
    }

    /// <summary>
    /// Creates the engine object hosted by a new element.
    /// </summary>
    public sealed record EngineObject : ElementSource
    {
        public EngineObject(Func<Beutl.Engine.EngineObject> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            Factory = factory;
        }

        public Func<Beutl.Engine.EngineObject> Factory { get; }
    }

    /// <summary>
    /// Creates a complete element from a saved element template.
    /// </summary>
    public sealed record ElementTemplate : ElementSource
    {
        public ElementTemplate(Func<Element> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            Factory = factory;
        }

        public Func<Element> Factory { get; }
    }

    /// <summary>
    /// Creates several complete elements, with the groups among them, from a template saved from them.
    /// </summary>
    /// <remarks>
    /// The elements keep their names, lengths and transforms, and where each starts and lies relative to the
    /// others; a description that asks for a name, a length or a position is refused. Like pasted elements, they go
    /// together to the free place nearest the requested start and layer.
    /// </remarks>
    public sealed record ElementSetTemplate : ElementSource
    {
        public ElementSetTemplate(Func<ElementSet> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            Factory = factory;
        }

        public Func<ElementSet> Factory { get; }
    }
}

/// <summary>
/// Describes how to create and place new timeline content from exactly one source.
/// </summary>
public sealed record ElementDescription
{
    /// <summary>
    /// Initializes an immutable element-add request.
    /// </summary>
    /// <param name="Length">
    /// The requested length. <see langword="null"/> lets the selected source handler preserve or
    /// choose the materialized length; handlers that require an explicit value reject it during
    /// preflight. Any value, including zero, explicitly requests that length.
    /// </param>
    public ElementDescription(
        MediaTime Start,
        MediaTime? Length,
        int Layer,
        ElementSource Source,
        string Name = "",
        Point? Position = null)
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Name);

        this.Start = Start;
        this.Length = Length;
        this.Layer = Layer;
        this.Source = Source;
        this.Name = Name;
        this.Position = Position;
    }

    public MediaTime Start { get; }

    public MediaTime? Length { get; }

    public int Layer { get; }

    public ElementSource Source { get; }

    public string Name { get; }

    /// <summary>
    /// An optional drawable translation. <see langword="null"/> preserves the source transform;
    /// a value of <c>(0, 0)</c> explicitly places the drawable at the origin.
    /// </summary>
    public Point? Position { get; init; }

    /// <summary>
    /// Resolves the element name: the explicit <see cref="Name"/> when set, otherwise the
    /// localized display name of <paramref name="fallbackType"/>.
    /// </summary>
    public string ResolveName(Type fallbackType) =>
        string.IsNullOrEmpty(Name) ? TypeDisplayHelpers.GetLocalizedName(fallbackType) : Name;
}
