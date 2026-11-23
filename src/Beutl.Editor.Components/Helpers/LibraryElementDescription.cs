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
    public static ElementDescription Create(Type type, MediaTime start, MediaTime length, int layer, Point? position = null)
    {
        if (type != typeof(Scene3D))
        {
            return new ElementDescription(start, length, layer,
                new ElementSource.EngineObject(() => (EngineObject)Activator.CreateInstance(type)!), Position: position);
        }

        // Place the scene and cube together so the portal always reaches the cube's layer.
        return new ElementDescription(start, Length: null, layer,
            new ElementSource.ElementSetTemplate(() => CreateScene3DSet(start, length, position)));
    }

    private static ElementSet CreateScene3DSet(MediaTime start, MediaTime length, Point? position)
    {
        var scene3D = new Scene3D();
        Element sceneElement = CreateElement(scene3D, start, length, layer: 0);
        sceneElement.Objects.OfType<PortalObject>().First().Count.Value = 1;
        if (CanonicalTransformLayout.SeedEmptyGroup(scene3D) is { } translate && position is { } pos)
        {
            translate.X.Value = pos.X;
            translate.Y.Value = pos.Y;
        }

        Element cubeElement = CreateElement(new Cube3D(), start, length, layer: 1);
        var set = new ElementSet();
        set.Elements.AddRange([sceneElement, cubeElement]);
        return set;
    }

    private static Element CreateElement(EngineObject value, MediaTime start, MediaTime length, int layer)
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
        return element;
    }
}
