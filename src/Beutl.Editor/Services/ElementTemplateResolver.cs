using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

public static class ElementTemplateResolver
{
    /// <summary>
    /// The templates a timeline adds as complete elements: one element's, and those saved from several.
    /// </summary>
    public static IEnumerable<ObjectTemplateItem> FindElementTemplates()
        => ObjectTemplateService.Instance.FindByBaseTypes(typeof(Element), typeof(ElementSet));

    /// <summary>
    /// Whether <see cref="CreateDescription"/> can add what <paramref name="template"/> holds to a scene.
    /// </summary>
    public static bool CanCreateDescription(ObjectTemplateItem template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return typeof(Element).IsAssignableFrom(template.ActualType)
               || typeof(ElementSet).IsAssignableFrom(template.ActualType)
               || typeof(EngineObject).IsAssignableFrom(template.ActualType);
    }

    public static ElementDescription CreateDescription(
        ObjectTemplateItem template,
        MediaTime start,
        int layer,
        MediaTime? lengthOverride = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (typeof(Element).IsAssignableFrom(template.ActualType))
        {
            return new ElementDescription(
                start,
                lengthOverride,
                layer,
                new ElementSource.ElementTemplate(() => CreateElement(template)));
        }

        if (typeof(ElementSet).IsAssignableFrom(template.ActualType))
        {
            return new ElementDescription(
                start,
                lengthOverride,
                layer,
                new ElementSource.ElementSetTemplate(() => CreateElementSet(template)));
        }

        if (typeof(EngineObject).IsAssignableFrom(template.ActualType))
        {
            return new ElementDescription(
                start,
                lengthOverride ?? MediaTime.FromSeconds(5),
                layer,
                new ElementSource.EngineObject(() => CreateEngineObject(template)),
                template.Name.Value);
        }

        throw new ArgumentException(
            $"Template type '{template.ActualType}' cannot be added to a scene.",
            nameof(template));
    }

    private static Element CreateElement(ObjectTemplateItem template)
    {
        Element source = template.CreateInstance() as Element
                         ?? throw new InvalidOperationException(
                             $"Template '{template.Name.Value}' did not create an element.");
        ObjectRegenerator.Regenerate(source, out Element result);
        return result;
    }

    // Regenerated as one graph, so the groups, and any reference between the elements, follow the new Ids.
    private static ElementSet CreateElementSet(ObjectTemplateItem template)
    {
        ElementSet source = template.CreateInstance() as ElementSet
                            ?? throw new InvalidOperationException(
                                $"Template '{template.Name.Value}' did not create an element set.");
        ObjectRegenerator.Regenerate(source, out ElementSet result);
        return result;
    }

    private static EngineObject CreateEngineObject(ObjectTemplateItem template)
    {
        EngineObject source = template.CreateInstance() as EngineObject
                              ?? throw new InvalidOperationException(
                                  $"Template '{template.Name.Value}' did not create an engine object.");
        ObjectRegenerator.Regenerate(source, source.GetType(), out ICoreSerializable result);
        return result as EngineObject
               ?? throw new InvalidOperationException(
                   $"Template '{template.Name.Value}' created an incompatible object.");
    }
}
