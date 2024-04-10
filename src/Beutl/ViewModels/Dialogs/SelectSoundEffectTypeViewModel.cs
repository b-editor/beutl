using Beutl.Audio.Effects;
using Beutl.Services;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectSoundEffectTypeViewModel : SelectLibraryItemDialogViewModel
{
    public SelectSoundEffectTypeViewModel()
        : base(KnownLibraryItemFormats.SoundEffect, typeof(ISoundEffect))
    {
    }
}
