using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Creates the built-in text element used when no saved caption template is selected.
/// </summary>
public sealed class DefaultTextCaptionElementFactory : ICaptionElementFactory
{
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
                    Text = { Value = cue.Text },
                    Size = { Value = 48 },
                    FontFamily = { Value = FontFamily },
                    AlignmentX = { Value = AlignmentX.Center },
                    AlignmentY = { Value = AlignmentY.Center },
                }),
        ];
    }
}

public static class CaptionTemplateDefaults
{
    /// <summary>
    /// Creates the built-in default template with a host-supplied element factory.
    /// </summary>
    public static CaptionTemplate CreateDefaultText(
        string name,
        ICaptionElementFactory elementFactory)
    {
        ArgumentNullException.ThrowIfNull(elementFactory);
        var descriptor = new CaptionTemplateDescriptor(
            CaptionTemplateIds.DefaultText,
            CaptionTemplateProviders.BuiltIn,
            name,
            order: int.MinValue);
        return new CaptionTemplate(descriptor, elementFactory, DefaultCaptionPlacementPolicy.Instance);
    }
}
