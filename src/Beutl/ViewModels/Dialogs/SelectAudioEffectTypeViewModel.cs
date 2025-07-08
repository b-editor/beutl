using Beutl.Audio.Effects;
using Beutl.Services;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectAudioEffectTypeViewModel : SelectLibraryItemDialogViewModel
{
    public SelectAudioEffectTypeViewModel()
        : base(KnownLibraryItemFormats.AudioEffect, typeof(IAudioEffect))
    {
    }
}
