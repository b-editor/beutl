using Beutl.Editor.Models;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Adapts a saved object template to caption element creation and binds each cue to a fresh object.
/// </summary>
public sealed class ObjectTemplateCaptionElementFactory<TObject> : ICaptionElementFactory
    where TObject : EngineObject
{
    private readonly Action<TObject, CaptionCue> _applyCue;

    public ObjectTemplateCaptionElementFactory(
        ObjectTemplateItem objectTemplate,
        Action<TObject, CaptionCue> applyCue)
    {
        ArgumentNullException.ThrowIfNull(objectTemplate);
        ArgumentNullException.ThrowIfNull(applyCue);
        if (!typeof(TObject).IsAssignableFrom(objectTemplate.ActualType))
        {
            throw new ArgumentException(
                $"Object template type '{objectTemplate.ActualType}' cannot be adapted as '{typeof(TObject)}'.",
                nameof(objectTemplate));
        }

        ObjectTemplate = objectTemplate;
        _applyCue = applyCue;
    }

    public ObjectTemplateItem ObjectTemplate { get; }

    public IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(context);

        return [context.CreateDescription(cue, () => CreateObject(cue))];
    }

    private TObject CreateObject(CaptionCue cue)
    {
        TObject source = ObjectTemplate.CreateInstance() as TObject
                         ?? throw new InvalidOperationException(
                             $"Failed to create caption object template '{ObjectTemplate.Name.Value}'.");

        ObjectRegenerator.Regenerate(source, source.GetType(), out ICoreSerializable regenerated);
        TObject result = regenerated as TObject
                         ?? throw new InvalidOperationException(
                             $"Caption object template '{ObjectTemplate.Name.Value}' created an incompatible object.");
        _applyCue(result, cue);
        return result;
    }
}

/// <summary>
/// Adapts saved text-block templates while keeping their authored transforms intact.
/// </summary>
public static class TextBlockCaptionTemplateAdapter
{
    public static CaptionTemplateContribution? TryCreate(ObjectTemplateItem objectTemplate)
    {
        ArgumentNullException.ThrowIfNull(objectTemplate);
        if (!typeof(TextBlock).IsAssignableFrom(objectTemplate.ActualType))
            return null;

        return new CaptionTemplateContribution(
            new CaptionTemplateId($"beutl.user.object-template.{objectTemplate.Id:N}"),
            CaptionTemplateProviders.User,
            objectTemplate.Name.Value,
            new ObjectTemplateCaptionElementFactory<TextBlock>(objectTemplate, ApplyCue),
            PreserveCaptionPlacementPolicy.Instance);
    }

    private static void ApplyCue(TextBlock textBlock, CaptionCue cue)
    {
        textBlock.Text.Expression = null;
        textBlock.Text.CurrentValue = cue.Text;
    }
}
