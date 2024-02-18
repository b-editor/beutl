using Avalonia.Controls;
using Avalonia.Input;

using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

public partial class VerifyTask : UserControl
{
    private bool _doubleClick;

    public VerifyTask()
    {
        InitializeComponent();
    }

    private void OnTaskNamePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_doubleClick)
        {
            if (DataContext is VerifyTaskModel model)
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
