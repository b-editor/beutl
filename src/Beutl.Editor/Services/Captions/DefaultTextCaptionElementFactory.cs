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
                    AlignmentX = { CurrentValue = AlignmentX.Center },
                    AlignmentY = { CurrentValue = AlignmentY.Center },
                }),
        ];
    }
}

public static class CaptionTemplateDefaults
{
    public static CaptionTemplateContribution CreateDefaultText(string name)
        => new(
            CaptionTemplateIds.DefaultText,
            CaptionTemplateProviders.BuiltIn,
            name,
            DefaultTextCaptionElementFactory.Instance,
            DefaultCaptionPlacementPolicy.Instance,
            order: int.MinValue);

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
