using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Adapts saved templates of one element or several that hold text objects. Every text object takes each cue's text,
/// and effects, transitions, placement and groups stay as the template has them.
/// </summary>
internal static class ElementCaptionTemplateAdapter
{
    public static CaptionTemplate? TryCreate(ObjectTemplateItem objectTemplate)
    {
        ArgumentNullException.ThrowIfNull(objectTemplate);
        ICaptionElementFactory factory;
        if (typeof(Element).IsAssignableFrom(objectTemplate.ActualType))
            factory = new ElementFactory(objectTemplate);
        else if (typeof(ElementSet).IsAssignableFrom(objectTemplate.ActualType))
            factory = new ElementSetFactory(objectTemplate);
        else
            return null;

        bool holdsText = objectTemplate.CreateInstance() switch
        {
            Element element => FindTexts(element).Any(),
            ElementSet set => set.Elements.Any(element => FindTexts(element).Any()),
            _ => false,
        };
        if (!holdsText)
            return null;

        return new CaptionTemplate(
            new CaptionTemplateDescriptor(
                new CaptionTemplateId($"beutl.user.object-template.{objectTemplate.Id:N}"),
                CaptionTemplateProviders.User,
                objectTemplate.Name.Value),
            factory,
            PreserveCaptionPlacementPolicy.Instance);
    }

    private static IEnumerable<TextBlock> FindTexts(Element element)
        => element.Objects.SelectMany(FindTexts);

    private static IEnumerable<TextBlock> FindTexts(EngineObject value) => value switch
    {
        TextBlock text => [text],
        DrawableGroup group => group.Children.SelectMany(FindTexts),
        _ => [],
    };

    private static void ApplyCue(Element element, CaptionCue cue)
    {
        foreach (TextBlock text in FindTexts(element))
        {
            TextBlockCaptionTemplateAdapter.ApplyCue(text, cue);
        }
    }

    // What lasts as long as a text object fits the cue with it, like a band behind the text, and if it starts too late to
    // be read in the cue, moves back to end with it, as the cue's words have to show. What does not, such as a short
    // flourish, keeps its length, and loses only what would outlast the cue.
    private static void FitToCue(ElementSet set, MediaTime cueLength, MediaTime minimumLength)
    {
        MediaTime origin = set.Elements.Min(element => element.Start.Value);
        HashSet<MediaTime> textLengths =
            [.. set.Elements.Where(element => FindTexts(element).Any()).Select(element => element.Length.Value)];
        foreach (Element element in set.Elements.ToArray())
        {
            MediaTime available = cueLength - (element.Start.Value - origin);
            if (textLengths.Contains(element.Length.Value))
            {
                if (available < minimumLength)
                {
                    element.Start.Value = origin + cueLength - minimumLength;
                    available = minimumLength;
                }

                element.Length.Value = available;
            }
            else if (available <= MediaTime.Zero)
                Remove(set, element);
            else if (element.Length.Value > available)
                element.Length.Value = available;
        }
    }

    private static void Remove(ElementSet set, Element element)
    {
        set.Elements.Remove(element);
        for (int i = set.Groups.Count - 1; i >= 0; i--)
        {
            ImmutableHashSet<Guid> rest = set.Groups[i].Remove(element.Id.Value);
            if (rest.Count < 2)
                set.Groups.RemoveAt(i);
            else
                set.Groups[i] = rest;
        }
    }

    private sealed class ElementFactory(ObjectTemplateItem template) : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context)
        {
            ArgumentNullException.ThrowIfNull(cue);
            ArgumentNullException.ThrowIfNull(context);

            return
            [
                new ElementDescription(
                    MediaTime.FromTimeSpan(cue.Start),
                    context.GetCueLength(cue),
                    context.Layer,
                    new ElementSource.ElementTemplate(() =>
                    {
                        Element element = ElementTemplateResolver.CreateElement(template);
                        ApplyCue(element, cue);
                        return element;
                    })),
            ];
        }
    }

    private sealed class ElementSetFactory(ObjectTemplateItem template) : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(CaptionCue cue, CaptionElementContext context)
        {
            ArgumentNullException.ThrowIfNull(cue);
            ArgumentNullException.ThrowIfNull(context);

            // Made here, not when the request goes in, as whoever places cues needs to see the layers it takes.
            ElementSet set = ElementTemplateResolver.CreateElementSet(template);
            foreach (Element element in set.Elements)
            {
                ApplyCue(element, cue);
            }

            FitToCue(set, context.GetCueLength(cue), context.MinimumLength);
            return
            [
                new ElementDescription(
                    MediaTime.FromTimeSpan(cue.Start),
                    null,
                    context.Layer,
                    new ElementSource.ElementSetTemplate(() => set)),
            ];
        }
    }
}
