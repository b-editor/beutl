using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Services;

public static class LibraryServiceExtensions
{
    public static MultipleTypeLibraryItem BindEngineObject<T>(this MultipleTypeLibraryItem self)
        where T : EngineObject
    {
        return self.Bind<T>(KnownLibraryItemFormats.EngineObject);
    }

    public static MultipleTypeLibraryItem BindEasing<T>(this MultipleTypeLibraryItem self)
        where T : Easing
    {
        return self.Bind<T>(KnownLibraryItemFormats.Easing);
    }

    public static MultipleTypeLibraryItem BindFilterEffect<T>(this MultipleTypeLibraryItem self)
        where T : FilterEffect
    {
        return self.Bind<T>(KnownLibraryItemFormats.FilterEffect);
    }

    public static MultipleTypeLibraryItem BindTransform<T>(this MultipleTypeLibraryItem self)
        where T : Transform
    {
        return self.Bind<T>(KnownLibraryItemFormats.Transform);
    }

    public static MultipleTypeLibraryItem BindDrawable<T>(this MultipleTypeLibraryItem self)
        where T : Drawable
    {
        return self.Bind<T>(KnownLibraryItemFormats.Drawable)
            .BindEngineObject<T>();
    }

    public static MultipleTypeLibraryItem BindSound<T>(this MultipleTypeLibraryItem self)
        where T : Sound
    {
        return self.Bind<T>(KnownLibraryItemFormats.Sound)
            .BindEngineObject<T>();
    }

    public static MultipleTypeLibraryItem BindAudioEffect<T>(this MultipleTypeLibraryItem self)
        where T : AudioEffect
    {
        return self.Bind<T>(KnownLibraryItemFormats.AudioEffect);
    }

    public static MultipleTypeLibraryItem BindBrush<T>(this MultipleTypeLibraryItem self)
        where T : Brush
    {
        return self.Bind<T>(KnownLibraryItemFormats.Brush);
    }

    public static MultipleTypeLibraryItem BindGeometry<T>(this MultipleTypeLibraryItem self)
        where T : Geometry
    {
        return self.Bind<T>(KnownLibraryItemFormats.Geometry);
    }

    public static GroupLibraryItem AddEngineObject<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : EngineObject
    {
        return self.Add<T>(KnownLibraryItemFormats.EngineObject, displayName, description);
    }

    public static GroupLibraryItem AddEasing<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : Easing
    {
        return self.Add<T>(KnownLibraryItemFormats.Easing, displayName, description);
    }

    public static GroupLibraryItem AddFilterEffect<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : FilterEffect
    {
        return self.Add<T>(KnownLibraryItemFormats.FilterEffect, displayName, description);
    }

    public static GroupLibraryItem AddTransform<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : Transform
    {
        return self.Add<T>(KnownLibraryItemFormats.Transform, displayName, description);
    }

    public static GroupLibraryItem AddDrawable<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : Drawable
    {
        return self.Add<T>(KnownLibraryItemFormats.Drawable, displayName, description);
    }

    public static GroupLibraryItem AddSound<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : Sound
    {
        return self.Add<T>(KnownLibraryItemFormats.Sound, displayName, description);
    }

    public static GroupLibraryItem AddAudioEffect<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : AudioEffect
    {
        return self.Add<T>(KnownLibraryItemFormats.AudioEffect, displayName, description);
    }

    public static GroupLibraryItem AddBrush<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : Brush
    {
        return self.Add<T>(KnownLibraryItemFormats.Brush, displayName, description);
    }

    public static GroupLibraryItem AddGeometry<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : Geometry
    {
        return self.Add<T>(KnownLibraryItemFormats.Geometry, displayName, description);
    }

}
