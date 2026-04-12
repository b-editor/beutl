using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

public static class ObjectTemplateCategoryResolver
{
    public const string ElementFormat = "Beutl.ProjectSystem.Element";

    public static (Type BaseType, string Format) Resolve(Type actualType)
    {
        if (actualType.IsAssignableTo(typeof(Element)))
            return (typeof(Element), ElementFormat);
        if (actualType.IsAssignableTo(typeof(FilterEffect)))
            return (typeof(FilterEffect), KnownLibraryItemFormats.FilterEffect);
        if (actualType.IsAssignableTo(typeof(Transform)))
            return (typeof(Transform), KnownLibraryItemFormats.Transform);
        if (actualType.IsAssignableTo(typeof(Drawable)))
            return (typeof(Drawable), KnownLibraryItemFormats.Drawable);
        if (actualType.IsAssignableTo(typeof(Sound)))
            return (typeof(Sound), KnownLibraryItemFormats.Sound);
        if (actualType.IsAssignableTo(typeof(AudioEffect)))
            return (typeof(AudioEffect), KnownLibraryItemFormats.AudioEffect);
        if (actualType.IsAssignableTo(typeof(Brush)))
            return (typeof(Brush), KnownLibraryItemFormats.Brush);
        if (actualType.IsAssignableTo(typeof(Geometry)))
            return (typeof(Geometry), KnownLibraryItemFormats.Geometry);
        if (actualType.IsAssignableTo(typeof(Pen)))
            return (typeof(Pen), KnownLibraryItemFormats.Pen);
        if (actualType.IsAssignableTo(typeof(EngineObject)))
            return (typeof(EngineObject), KnownLibraryItemFormats.EngineObject);

        return (actualType, actualType.FullName ?? actualType.Name);
    }
}
