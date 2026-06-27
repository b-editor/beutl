using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Shapes;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

public static class TypeRegistration
{
    private static readonly object s_lock = new();
    private static bool s_registered;

    public static void EnsureRegistered()
    {
        lock (s_lock)
        {
            if (s_registered)
            {
                return;
            }

            LibraryService.Current.Register<SourceImage>(KnownLibraryItemFormats.Drawable, nameof(SourceImage));
            LibraryService.Current.Register<SourceVideo>(KnownLibraryItemFormats.Drawable, nameof(SourceVideo));
            LibraryService.Current.Register<TextBlock>(KnownLibraryItemFormats.Drawable, nameof(TextBlock));
            LibraryService.Current.Register<RectShape>(KnownLibraryItemFormats.Drawable, nameof(RectShape));
            LibraryService.Current.Register<EllipseShape>(KnownLibraryItemFormats.Drawable, nameof(EllipseShape));
            LibraryService.Current.Register<DrawableGroup>(KnownLibraryItemFormats.Drawable, nameof(DrawableGroup));
            LibraryService.Current.Register<ParticleEmitter>(KnownLibraryItemFormats.Drawable, nameof(ParticleEmitter));

            LibraryService.Current.Register<SourceSound>(KnownLibraryItemFormats.Sound, nameof(SourceSound));
            LibraryService.Current.Register<SoundGroup>(KnownLibraryItemFormats.Sound, nameof(SoundGroup));

            LibraryService.Current.Register<Brightness>(KnownLibraryItemFormats.FilterEffect, nameof(Brightness));
            LibraryService.Current.Register<Blur>(KnownLibraryItemFormats.FilterEffect, nameof(Blur));
            LibraryService.Current.Register<DropShadow>(KnownLibraryItemFormats.FilterEffect, nameof(DropShadow));
            LibraryService.Current.Register<Saturate>(KnownLibraryItemFormats.FilterEffect, nameof(Saturate));

            s_registered = true;
        }
    }
}
