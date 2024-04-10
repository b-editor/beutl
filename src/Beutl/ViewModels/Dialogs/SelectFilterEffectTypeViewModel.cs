using Beutl.Graphics.Effects;
using Beutl.Services;

namespace Beutl.ViewModels.Dialogs;

public sealed class SelectFilterEffectTypeViewModel : SelectLibraryItemDialogViewModel
{
    public SelectFilterEffectTypeViewModel()
        : base(KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect))
    {
    }
}
