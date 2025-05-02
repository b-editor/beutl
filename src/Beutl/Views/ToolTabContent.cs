using Avalonia.Controls;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed class ToolTabContent : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is null)
        {
            Content = null;
            return;
        }

        if (DataContext is not ToolTabViewModel viewModel ||
            !viewModel.Context.Extension.TryCreateContent(viewModel.EditViewModel, out Control? control))
        {
            control = new TextBlock()
            {
                Text = $"""
                        Error:
                            {Message.CannotDisplayThisContext}
                        """
            };
        }
        else
        {
            var cm = App.GetContextCommandManager();
            cm?.Attach(control, viewModel.Context.Extension);
            control.DataContext = viewModel.Context;
        }

        Content = control;
    }
}
