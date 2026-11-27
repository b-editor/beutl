using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Logging;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentTimelineView : UserControl
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentTimelineView>();

    public AgentTimelineView()
    {
        InitializeComponent();
    }

    private async void OnCopyResponse(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentAssistantItem { CanCopy: true } item }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(item.Text);
            }
            catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
            {
                s_logger.LogWarning(ex, "Could not copy the agent response to the clipboard.");
            }
        }
    }

    private void OnTimelineScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TimelineScroll) || (e.ExtentDelta.Y <= 0 && e.ViewportDelta.Y >= 0))
            return;

        double previousOffset = TimelineScroll.Offset.Y - e.OffsetDelta.Y;
        double previousBottom = TimelineScroll.Extent.Height - e.ExtentDelta.Y
            - (TimelineScroll.Viewport.Height - e.ViewportDelta.Y);
        if (previousBottom - previousOffset <= 24)
            TimelineScroll.ScrollToEnd();
    }
}
