using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

// The step badge, the line and the title of an install task. Double-clicking the title shows or hides the details.
public partial class TaskStep : UserControl
{
    public static readonly StyledProperty<int> StepProperty =
        AvaloniaProperty.Register<TaskStep, int>(nameof(Step));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TaskStep, string?>(nameof(Title));

    public static readonly StyledProperty<Control?> DetailsProperty =
        AvaloniaProperty.Register<TaskStep, Control?>(nameof(Details));

    private bool _doubleClick;

    public TaskStep()
    {
        InitializeComponent();
    }

    public int Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Control? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

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
