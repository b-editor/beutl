using Avalonia.Controls;
using Avalonia.Input;

using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

public partial class ResolveTask : UserControl
{
    private bool _doubleClick;

    public ResolveTask()
    {
        InitializeComponent();
    }

    private void OnTaskNamePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_doubleClick)
        {
            if (DataContext is ResolveTaskModel model)
            {
                model.ShowDetails.Value = !model.ShowDetails.Value;
            }

            _doubleClick = false;
        }
    }

    private void OnTaskNamePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _doubleClick = true;
        }
    }
}
