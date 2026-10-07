using Avalonia.Controls;

using Avalonia.Threading;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class EditView : UserControl
{
    public EditView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            if (DataContext is EditViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
                await vm.ShowMissingMediaAsync(owner, onlyOnFirstOpen: true);
        });
    }
}
