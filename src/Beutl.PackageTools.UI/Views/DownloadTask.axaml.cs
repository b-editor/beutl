using Avalonia.Controls;
using Avalonia.Input;

using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

public partial class DownloadTask : UserControl
{
    private bool _doubleClick;

    public DownloadTask()
    {
        InitializeComponent();
    }

    private void OnTaskNamePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_doubleClick)
        {
            if (DataContext is DownloadTaskModel model)
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
