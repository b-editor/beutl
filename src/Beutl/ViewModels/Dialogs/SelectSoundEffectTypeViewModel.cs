using Beutl.Audio.Effects;
using Beutl.Services;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectSoundEffectTypeViewModel : SelectLibraryItemDialogViewModel
{
    // Todo: String resource
    public SelectSoundEffectTypeViewModel()
        : base(KnownLibraryItemFormats.SoundEffect, typeof(ISoundEffect), Strings.SelectFilterEffect)
    {
    }
}
