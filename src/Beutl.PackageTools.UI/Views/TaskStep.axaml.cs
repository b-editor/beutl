using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

// The step badge, the line and the title of an install task. Double-clicking the title shows or hides the details.
public partial class TaskStep : UserControl
{
    private bool _doubleClick;

    public TaskStep()
    {
        InitializeComponent();
    }

    [GeneratedStyledProperty]
    public partial int Step { get; set; }

    [GeneratedStyledProperty]
    public partial string? Title { get; set; }

    [GeneratedStyledProperty]
    public partial Control? Details { get; set; }

    private void OnTaskNamePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_doubleClick)
        {
            if (DataContext is ITaskStepModel model)
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
