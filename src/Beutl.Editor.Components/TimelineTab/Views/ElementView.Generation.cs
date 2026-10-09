using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public partial class ElementView
{
    // The AI submenu is built for the element each time the menu opens: what it offers
    // depends on the media the element plays and on whether AI made it.
    private void PopulateAiMenu(ElementViewModel viewModel)
    {
        aiActions.Items.Clear();
        TimelineTabViewModel timeline = viewModel.Timeline;
        if (!timeline.IsGenerationAvailable)
        {
            aiActions.IsVisible = false;
            return;
        }

        Element element = viewModel.Model;
        bool afterImageEdits = false;
        foreach (TimelineAiAction action in TimelineAiActions.For(element))
        {
            bool imageEdit = TimelineAiActions.ImageTaskOf(action) is not null;
            if (afterImageEdits && !imageEdit)
                aiActions.Items.Add(new FAMenuFlyoutSeparator());
            afterImageEdits = imageEdit;

            var item = new FAMenuFlyoutItem { Text = LabelOf(action) };
            item.Click += (_, _) => _ = timeline.StartAiActionAsync(element, action);
            UsageTracking.SetFeature(item, $"Timeline.Ai.{action}");
            aiActions.Items.Add(item);
        }

        if (aiActions.Items.Count > 0 && CanRegenerate(element))
            aiActions.Items.Add(new FAMenuFlyoutSeparator());
        AddGenerationItems(aiActions.Items, viewModel);

        aiActions.IsVisible = aiActions.Items.Count > 0;
    }

    private static bool CanRegenerate(Element element)
        => element.Generation is { } generation
           && TimelineGenerationSpec.FromJson(generation.Parameters) is not null;

    // Regenerate, change and regenerate, and the takes: shared by the menu and the AI badge.
    private static void AddGenerationItems(System.Collections.IList items, ElementViewModel viewModel)
    {
        TimelineTabViewModel timeline = viewModel.Timeline;
        Element element = viewModel.Model;
        if (!CanRegenerate(element) || timeline.GenerationService is not { } service)
            return;
        ElementGeneration generation = element.Generation!;

        var regenerate = new FAMenuFlyoutItem { Text = Strings.AiTimelineRegenerate, IsEnabled = viewModel.IsEditable.Value };
        regenerate.Click += (_, _) => timeline.Regenerate(element, edit: false);
        UsageTracking.SetFeature(regenerate, "Timeline.Ai.Regenerate");
        items.Add(regenerate);

        var change = new FAMenuFlyoutItem { Text = Strings.AiTimelineEditAndRegenerate, IsEnabled = viewModel.IsEditable.Value };
        change.Click += (_, _) => timeline.Regenerate(element, edit: true);
        UsageTracking.SetFeature(change, "Timeline.Ai.EditAndRegenerate");
        items.Add(change);

        var takes = new FAMenuFlyoutSubItem { Text = Strings.AiTimelineTakes };
        int number = 0;
        foreach (ElementGenerationTake take in generation.Takes)
        {
            string label = take.IsOriginal
                ? Strings.AiTimelineOriginalTake
                : $"{++number}. {take.Summary ?? string.Empty} ({take.CreatedAt.ToLocalTime():g})";
            var item = new FARadioMenuFlyoutItem
            {
                Text = label,
                GroupName = "AiTakes",
                IsChecked = take.Id == generation.ActiveTakeId,
                IsEnabled = viewModel.IsEditable.Value,
            };
            item.Click += (_, _) => service.SelectTake(element, take);
            takes.Items.Add(item);
        }

        items.Add(takes);
    }

    private void AiBadge_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel || sender is not Avalonia.Controls.Control badge)
            return;
        var flyout = new FAMenuFlyout();
        AddGenerationItems(flyout.Items, viewModel);
        if (flyout.Items.Count > 0)
            flyout.ShowAt(badge);
    }

    private static string LabelOf(TimelineAiAction action) => action switch
    {
        TimelineAiAction.RemoveBackground => Strings.AiEditRemoveBackground,
        TimelineAiAction.Upscale => Strings.AiEditUpscale,
        TimelineAiAction.Restyle => Strings.AiEditRestyle,
        TimelineAiAction.RemoveObject => Strings.AiEditRemoveObject,
        TimelineAiAction.Outpaint => Strings.AiEditOutpaint,
        TimelineAiAction.VideoFromImage => Strings.AiTimelineVideoFromImage,
        TimelineAiAction.ContinueVideo => Strings.AiTimelineContinueVideo,
        TimelineAiAction.ExtendVideo => Strings.AiTimelineExtendVideo,
        TimelineAiAction.EditVideo => Strings.AiTimelineEditVideo,
        TimelineAiAction.Subtitles => Strings.AiTimelineSubtitles,
        _ => action.ToString(),
    };
}
