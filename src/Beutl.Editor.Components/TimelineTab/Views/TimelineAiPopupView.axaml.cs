using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.TimelineTab.Generative;

namespace Beutl.Editor.Components.TimelineTab.Views;

public partial class TimelineAiPopupView : UserControl
{
    public TimelineAiPopupView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Typing the prompt is what the popup is for.
        if (PromptBox.IsVisible)
            PromptBox.Focus();
    }

    private async void ChooseLastFrame_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TimelineAiPopupViewModel viewModel)
            return;
        // An event handler: nothing above it would catch a failure, and the app would end.
        try
        {
            if (viewModel.LastFrame.Value is not null)
                viewModel.ClearLastFrame();
            else
                await viewModel.ChooseLastFrameAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
