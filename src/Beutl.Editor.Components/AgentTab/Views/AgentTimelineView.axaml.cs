using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentTimelineView : UserControl
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentTimelineView>();

    public AgentTimelineView()
    {
        InitializeComponent();
    }

    internal Task OpenLinkAsync(AgentMarkdownLink link)
        => (DataContext as AgentTabViewModel)?.OpenMarkdownLinkAsync(link) ?? Task.CompletedTask;

    private async void OnCopyMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentMessageItem item })
            await AgentClipboardHelper.TryCopyTextAsync(this, item.Text, s_logger, "Could not copy the agent message to the clipboard.");
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
            && !await AgentImageLightbox.OpenAsync(button, preview))
            attachment.RemoveUnavailablePreview();
    }

    private static async void OnOpenAssistantImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentImagePreview preview } button)
            await AgentImageLightbox.OpenAsync(button, preview);
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
