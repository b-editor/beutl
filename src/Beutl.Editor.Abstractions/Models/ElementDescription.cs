using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Models;

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
}

/// <summary>
/// Describes how to create and place new timeline content from exactly one source.
/// </summary>
public sealed record ElementDescription
{
    /// <summary>
    /// Initializes an immutable element-add request.
    /// </summary>
    /// <param name="Start">The requested timeline start.</param>
    /// <param name="Length">
    /// The requested length. For an <see cref="ElementSource.ElementTemplate"/>,
    /// <see langword="null"/> preserves the factory-provided length while any value, including
    /// zero, explicitly replaces it. Other source variants require a value.
    /// </param>
    /// <param name="Layer">The requested timeline layer.</param>
    /// <param name="Source">The single source from which the element is created.</param>
    /// <param name="Name">An optional element name.</param>
    /// <param name="Position">An optional drawable translation.</param>
    public ElementDescription(
        TimeSpan Start,
        TimeSpan? Length,
        int Layer,
        ElementSource Source,
        string Name = "",
        Point? Position = null)
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Name);
        if (Length is null && Source is not ElementSource.ElementTemplate)
        {
            throw new ArgumentException(
                "Only an element template source can preserve its source length.",
                nameof(Length));
        }

        this.Start = Start;
        this.Length = Length;
        this.Layer = Layer;
        this.Source = Source;
        this.Name = Name;
        this.Position = Position;
    }

    public TimeSpan Start { get; }

    public TimeSpan? Length { get; }

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
