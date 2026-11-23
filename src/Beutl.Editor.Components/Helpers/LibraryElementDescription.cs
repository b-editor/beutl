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
    internal static ElementDescription Create(Type type, MediaTime start, MediaTime length, int layer, Point? position = null)
    {
        if (type == typeof(Scene3D))
        {
            // The set placement keeps the portal and cube on adjacent rows and records them in one add transaction.
            return new ElementDescription(start, null, layer,
                new ElementSource.ElementSetTemplate(() => CreateScene3DSet(length, position)));
        }

        return new ElementDescription(start, length, layer,
            new ElementSource.EngineObject(() => (EngineObject)Activator.CreateInstance(type)!), Position: position);
    }

    private static ElementSet CreateScene3DSet(MediaTime length, Point? position)
    {
        var scene = new Scene3D();
        if (position is { } point)
            AddToGroupHelper.Add(scene.Transform, new TranslateTransform(point));

        Element container = CreateElement(scene, GraphicsStrings.Scene3D, 0, length);
        container.Objects.OfType<PortalObject>().Single().Count.Value = 1;
        Element cube = CreateElement(new Cube3D(), GraphicsStrings.Cube3D, 1, length);
        return new ElementSet { Elements = { container, cube } };
    }

    private static Element CreateElement(EngineObject obj, string name, int layer, MediaTime length)
    {
        var element = new Element
        {
            Name = { Value = name },
            AccentColor = { Value = ElementAccentColorGenerator.GenerateColor(obj.GetType().FullName!) },
            Length = { Value = length },
            ZIndex = { Value = layer },
        };
        element.AddObject(obj);
        return element;
    }
}
