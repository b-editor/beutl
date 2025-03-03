using Beutl.Graphics;
using Beutl.Services;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectDrawableTypeViewModel : SelectLibraryItemDialogViewModel
{
    public SelectDrawableTypeViewModel()
        : base(KnownLibraryItemFormats.Drawable, typeof(Drawable))
    {
    }
}
