using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

internal static class LibraryElementDescription
{
    private const int DefaultPortalCount = 5;

    public static ElementDescription Create(Type type, MediaTime start, MediaTime length, int layer, Point? position = null)
    {
        if (type == typeof(Scene3D))
        {
            // Place the scene and cube together so the portal always reaches the cube's layer.
            return new ElementDescription(start, Length: null, layer,
                new ElementSource.ElementSetTemplate(() => CreateScene3DSet(start, length, position)));
        }

        if (typeof(IFlowOperator).IsAssignableFrom(type))
        {
            // The factory applies the drop position while setting up the drawable's transforms.
            return new ElementDescription(start, length, layer,
                new ElementSource.ElementTemplate(() =>
                    CreateElement((EngineObject)Activator.CreateInstance(type)!, start, length, layer, position)),
                Position: null);
        }

        return new ElementDescription(start, length, layer,
            new ElementSource.EngineObject(() => (EngineObject)Activator.CreateInstance(type)!), Position: position);
    }

    private static ElementSet CreateScene3DSet(MediaTime start, MediaTime length, Point? position)
    {
        Element sceneElement = CreateElement(new Scene3D(), start, length, layer: 0, position: position);
        Element cubeElement = CreateElement(new Cube3D(), start, length, layer: 1);
        var set = new ElementSet();
        set.Elements.AddRange([sceneElement, cubeElement]);
        return set;
    }

    private static Element CreateElement(EngineObject value, MediaTime start, MediaTime length, int layer, Point? position = null)
    {
        Type type = value.GetType();
        var element = new Element
        {
            Start = { Value = start },
            Length = { Value = length },
            ZIndex = { Value = layer },
            Name = { Value = TypeDisplayHelpers.GetLocalizedName(type) },
            AccentColor = { Value = ElementAccentColorGenerator.GenerateColor(type.FullName ?? type.Name) },
        };
        element.AddObject(value);
        if (value is IFlowOperator)
        {
            element.Objects.OfType<PortalObject>().First().Count.Value = DefaultPortalCount;
        }

        TranslateTransform? translate = CanonicalTransformLayout.SeedEmptyGroup(value);
        if (position is { } pos)
        {
            if (translate is not null)
            {
                translate.X.Value = pos.X;
                translate.Y.Value = pos.Y;
            }
            else if (value is Drawable drawable)
            {
                AddToGroupHelper.Add(drawable.Transform, new TranslateTransform(pos));
            }
        }

        return element;
    }
}
