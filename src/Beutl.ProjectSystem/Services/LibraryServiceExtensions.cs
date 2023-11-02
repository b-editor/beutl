using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeTree;
using Beutl.Operation;

namespace Beutl.Services;

public static class LibraryServiceExtensions
{
    public static MultipleTypeLibraryItem BindSourceOperator<T>(this MultipleTypeLibraryItem self)
        where T : SourceOperator
    {
        return self.Bind<T>(KnownLibraryItemFormats.SourceOperator);
    }

    public static MultipleTypeLibraryItem BindNode<T>(this MultipleTypeLibraryItem self)
        where T : Node
    {
        return self.Bind<T>(KnownLibraryItemFormats.Node);
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
        return self.Bind<T>(KnownLibraryItemFormats.Drawable);
    }

    public static MultipleTypeLibraryItem BindSound<T>(this MultipleTypeLibraryItem self)
        where T : Sound
    {
        return self.Bind<T>(KnownLibraryItemFormats.Sound);
    }

    public static MultipleTypeLibraryItem BindSoundEffect<T>(this MultipleTypeLibraryItem self)
        where T : SoundEffect
    {
        return self.Bind<T>(KnownLibraryItemFormats.SoundEffect);
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

    public static GroupLibraryItem AddSourceOperator<T>(this GroupLibraryItem self, string displayName, string? description = null)
        where T : SourceOperator
    {
        return self.Add<T>(KnownLibraryItemFormats.SourceOperator, displayName, description);
    }

}
