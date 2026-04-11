using Avalonia.Controls;
using Beutl.ViewModels.Dock;

namespace Beutl.Views;

/// <summary>
/// Hosts the Control produced by a <see cref="BeutlToolDockable"/>'s underlying
/// <see cref="IToolContext"/> via its <see cref="ToolTabExtension"/>.
/// </summary>
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

        if (DataContext is not BeutlToolDockable dockable ||
            !dockable.ToolContext.Extension.TryCreateContent(dockable.EditViewModel, out Control? control))
        {
            control = new TextBlock
            {
                Text = $"""
                        Error:
                            {MessageStrings.CannotDisplayContext}
                        """
            };
        }
        else
        {
            var cm = App.GetContextCommandManager();
            cm?.Attach(control, dockable.ToolContext.Extension);
            control.DataContext = dockable.ToolContext;
        }

        Content = control;
    }
}
