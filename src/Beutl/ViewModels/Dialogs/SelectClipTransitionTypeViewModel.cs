using Beutl.Graphics.Transitions;
using Beutl.Services;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectClipTransitionTypeViewModel : SelectLibraryItemDialogViewModel
{
    public SelectClipTransitionTypeViewModel()
        : base(KnownLibraryItemFormats.ClipTransition, typeof(ClipTransition))
    {
    }
}
