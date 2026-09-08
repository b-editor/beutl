using Beutl.Editor.Models;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

public static class ElementTemplateResolver
{
    public static ElementDescription CreateDescription(
        ObjectTemplateItem template,
        TimeSpan start,
        int layer,
        TimeSpan? lengthOverride = null)
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

        if (typeof(EngineObject).IsAssignableFrom(template.ActualType))
        {
            return new ElementDescription(
                start,
                lengthOverride ?? TimeSpan.FromSeconds(5),
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
