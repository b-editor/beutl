using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels.Dock;

namespace Beutl.Views;

/// <summary>
/// Hosts the Control produced by a <see cref="BeutlToolDockable"/>'s underlying
/// <see cref="IToolContext"/> via its <see cref="ToolTabExtension"/>.
/// </summary>
public sealed class ToolTabContent : ContentControl
{
    public ToolTabContent()
    {
        AddHandler(PointerPressedEvent, (_, _) => RecordInteraction(), RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => RecordInteraction(), RoutingStrategies.Bubble);
    }

    private void RecordInteraction()
    {
        if (DataContext is BeutlToolDockable dockable) dockable.RecordInteraction();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is null)
        {
            Content = null;
            return;
        }

        if (DataContext is not BeutlToolDockable dockable)
        {
            Content = CreateErrorContent();
            return;
        }

        Control? control = dockable.ToolContext.Extension.ReuseContentAcrossActivation
            ? dockable.ToolContent
            : null;

        if (control is null)
        {
            if (!dockable.ToolContext.Extension.TryCreateContent(dockable.EditViewModel, out control))
            {
                Content = CreateErrorContent();
                return;
            }

            var cm = AppHelper.GetContextCommandManager?.Invoke();
            cm?.Attach(control, dockable.ToolContext.Extension);
            control.DataContext = dockable.ToolContext;
            if (dockable.ToolContext.Extension.ReuseContentAcrossActivation)
            {
                dockable.ToolContent = control;
            }
        }

        if (control.Parent is ContentControl previousOwner
            && !ReferenceEquals(previousOwner, this)
            && ReferenceEquals(previousOwner.Content, control))
        {
            previousOwner.Content = null;
        }

        Content = control;
    }

    private static TextBlock CreateErrorContent()
    {
        return new TextBlock
        {
            Text = $"""
                    Error:
                        {MessageStrings.CannotDisplayContext}
                    """
        };
    }
}
