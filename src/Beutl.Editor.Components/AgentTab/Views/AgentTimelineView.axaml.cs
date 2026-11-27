using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    private async void OnCopyMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentMessageItem item }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(item.Text);
            }
            catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
            {
                s_logger.LogWarning(ex, "Could not copy the agent message to the clipboard.");
            }
        }
    }

    private async void OnEditMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentUserItem item } && DataContext is AgentTabViewModel model)
        {
            await model.EditFromMessageAsync(item);
            this.FindAncestorOfType<AgentTabView>()?.FocusComposer();
        }
    }

    private async void OnForkMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentMessageItem item } && DataContext is AgentTabViewModel model)
            await model.ForkFromMessageAsync(item);
    }

    private async void OnRetryMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentAssistantItem item } && DataContext is AgentTabViewModel model)
            await model.RetryMessageAsync(item);
    }

    private static async void OnOpenSentImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentMessageAttachmentItem { Preview: { } preview } attachment } button
            && !await AgentImageLightboxWindow.OpenAsync(button, preview))
            attachment.RemoveUnavailablePreview();
    }

    private void OnTimelineScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TimelineScroll))
            return;

        if (e.ExtentDelta.Y > 0 || e.ViewportDelta.Y < 0)
        {
            double previousOffset = TimelineScroll.Offset.Y - e.OffsetDelta.Y;
            double previousBottom = TimelineScroll.Extent.Height - e.ExtentDelta.Y
                - (TimelineScroll.Viewport.Height - e.ViewportDelta.Y);
            if (previousBottom - previousOffset <= 24)
                TimelineScroll.ScrollToEnd();
        }
        ScrollToBottomButton.IsVisible = TimelineScroll.Extent.Height - TimelineScroll.Viewport.Height - TimelineScroll.Offset.Y > 48;
    }

    private void OnScrollToBottom(object? sender, RoutedEventArgs e)
    {
        if (ScrollToBottomButton.IsVisible)
        {
            TimelineScroll.ScrollToEnd();
            ScrollToBottomButton.IsVisible = false;
        }
    }
}
