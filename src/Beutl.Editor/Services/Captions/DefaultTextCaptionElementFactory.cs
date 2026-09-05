using Beutl.Editor.Models;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Creates the built-in text element used when no saved caption template is selected.
/// </summary>
public sealed class DefaultTextCaptionElementFactory : ICaptionElementFactory
{
    public static DefaultTextCaptionElementFactory Instance { get; } = new();

    /// <summary>
    /// Initializes a factory that uses the rendering engine's default font family.
    /// </summary>
    public DefaultTextCaptionElementFactory()
        : this(Beutl.Media.FontFamily.Default)
    {
    }

    /// <summary>
    /// Initializes a factory that uses <paramref name="fontFamily"/> for every created caption.
    /// </summary>
    public DefaultTextCaptionElementFactory(FontFamily fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        FontFamily = fontFamily;
    }

    /// <summary>
    /// Gets the font family assigned to created captions.
    /// </summary>
    public FontFamily FontFamily { get; }

    public IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);

        return
        [
            context.CreateDescription(
                cue,
                () => new TextBlock
                {
                    Text = { CurrentValue = cue.Text },
                    Size = { CurrentValue = 48 },
                    FontFamily = { CurrentValue = FontFamily },
                    AlignmentX = { CurrentValue = AlignmentX.Center },
                    AlignmentY = { CurrentValue = AlignmentY.Center },
                }),
        ];
    }
}

public static class CaptionTemplateDefaults
{
    public static CaptionTemplateContribution CreateDefaultText(string name)
        => CreateDefaultText(name, DefaultTextCaptionElementFactory.Instance);

    /// <summary>
    /// Creates the built-in default template with a host-supplied element factory.
    /// </summary>
    public static CaptionTemplateContribution CreateDefaultText(
        string name,
        ICaptionElementFactory elementFactory)
    {
        ArgumentNullException.ThrowIfNull(elementFactory);
        return new(
            CaptionTemplateIds.DefaultText,
            CaptionTemplateProviders.BuiltIn,
            name,
            elementFactory,
            DefaultCaptionPlacementPolicy.Instance,
            order: int.MinValue);
    }

    public static CaptionTemplateContribution CreateText(
        CaptionTemplateId id,
        CaptionTemplateProviderId providerId,
        string name,
        int order = 0)
        => new(
            id,
            providerId,
            name,
            DefaultTextCaptionElementFactory.Instance,
            DefaultCaptionPlacementPolicy.Instance,
            order);
}
