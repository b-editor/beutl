using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static class ElementDescriptionMaterializer
{
    internal static Element CreateElementFor(ElementDescription description, EngineObject value, string name)
    {
        Type type = value.GetType();
        var element = new Element
        {
            Name = { Value = name },
            Start = { Value = description.Start },
            Length = { Value = description.Length
                ?? throw new InvalidOperationException("A non-template element source requires a length.") },
            ZIndex = { Value = description.Layer },
            AccentColor = { Value = ElementAccentColorGenerator.GenerateColor(type.FullName ?? type.Name) },
        };
        element.AddObject(value);
        if (CanonicalTransformLayout.SeedEmptyGroup(value) is { } translate)
        {
            if (description.Position is { } position)
            {
                translate.X.Value = position.X;
                translate.Y.Value = position.Y;
            }
        }
        else if (value is Drawable drawable && description.Position is { } position)
        {
            CanonicalTransformLayout.Insert(drawable.Transform, new TranslateTransform(position));
        }

        return element;
    }

    internal static void ApplyTemplatePlacement(Element element, ElementDescription description)
    {
        element.Start.Value = description.Start;
        if (description.Length is { } length)
        {
            element.Length.Value = length;
        }
        element.ZIndex.Value = description.Layer;
        if (!string.IsNullOrWhiteSpace(description.Name))
        {
            element.Name.Value = description.Name;
        }
        if (description.Position is { } position)
        {
            foreach (Drawable drawable in element.Objects.OfType<Drawable>())
            {
                CanonicalTransformLayout.Insert(drawable.Transform, new TranslateTransform(position));
            }
        }
    }
}
