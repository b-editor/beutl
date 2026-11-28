using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Language;
using Beutl.Testing.Headless;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentTabPhase3Tests
{
    [AvaloniaTest]
    public async Task RendersSentAttachmentsReadGroupHistoryAndCodeAndOpensAllImageSources()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace
        {
            TransactionCount = 2,
            TransactionChanges = ["Move opening title into the safe area", "Shorten the title fade to 12 frames"]
        };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        string framePath = Path.Combine(workspace.Root, "frame.png");
        string encoded = CreateFrame(framePath);
        string notesPath = Path.Combine(workspace.Root, "timing-notes.txt");
        await File.WriteAllTextAsync(notesPath, "Keep the title inside the safe area.");

        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now, "Check the title against the edge of the frame."));
            foreach (var (id, name, server) in new[] { ("1", "Read", (string?)null), ("2", "get_scene", "beutl"), ("3", "inspect_element", "beutl") })
            {
                workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now.AddSeconds(1.8), id, name, server));
                workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddSeconds(1.8), id, AgentToolStatus.Completed));
            }
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "frame", "render_still", "beutl"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "frame", AgentToolStatus.Completed,
                [new AgentToolOutput("image", Data: encoded)]));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, """
                The opening title now sits inside the safe area. The fade ends after 12 frames.

                ```json
                {
                  "titleFadeFrames": 12
                }
                ```
                """));
            workspace.Bridge.Emit(new AgentTurnUsageUpdated(thread, turn, now, 1024, 216, 800, "GPT-6.2 Sol"));
            await Task.Delay(350, ct);
        };

        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 995);
        try
        {
            window.Show();
            await model.CaptureFrameAsync();
            await model.AddFilesAsync([notesPath]);
            HeadlessTestHelpers.Render(5);
            Click(Find<Button>(view, "ComposerImageThumbnail"));
            var preview = await LightboxAsync(window);
            Assert.That(Find<Border>(preview, "Backdrop").Bounds.Size, Is.EqualTo(window.ClientSize));
            HeadlessTestHelpers.Click(Find<Border>(preview, "Backdrop"), new Point(4, 4), ticks: 3);
            await HeadlessTestHelpers.WaitUntilAsync(() => !HasLightbox(window));

            model.Prompt = "Check the opening title against this frame and my timing notes.";
            await model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => TryFind<Button>(view, "PART_CopyButton") is { IsEffectivelyVisible: true });
            HeadlessTestHelpers.Render(5);
            var sender = model.Timeline.OfType<AgentUserItem>().Single();
            var bubble = Find<Border>(view, "SentUserBubble", sender);
            var footer = Find<StackPanel>(view, "UserMessageFooter", sender);
            Assert.That(footer.TranslatePoint(default, bubble)!.Value.Y, Is.GreaterThanOrEqualTo(bubble.Bounds.Height));
            Assert.That(Find<Button>(footer, "CopyUserMessage"), Is.Not.Null);
            Assert.That(Find<Button>(footer, "EditUserMessage"), Is.Not.Null);
            Assert.That(Find<Button>(footer, "ForkUserMessage"), Is.Not.Null);
            Assert.That(footer.Children.OfType<TextBlock>().Single().Text, Is.EqualTo(sender.TimeLabel));
            Assert.That(sender.Entry.Attachments!.Select(file => file.Path), Is.EqualTo(new[] { framePath, notesPath }));
            Assert.That(Find<Button>(view, "SentImageThumbnail").IsEffectivelyVisible, Is.True);
            Assert.That(view.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "SentFileChip"
                && border.IsEffectivelyVisible && border.DataContext is AgentMessageAttachmentItem { Label: "timing-notes.txt" }), Is.True);

            var stats = Find<ToggleButton>(view, "TurnStatsToggle");
            Assert.That(stats.IsEffectivelyVisible, Is.True);
            Assert.That(stats.IsChecked, Is.False);
            Click(stats);
            HeadlessTestHelpers.Render(4);

            var group = model.Timeline.OfType<AgentToolGroupItem>().Single();
            var toggle = Find<ToggleButton>(view, "ToolGroupToggle");
            Assert.That(group.Tools.Select(tool => tool.ToolName), Is.EqualTo(new[] { "Read", "get_scene", "inspect_element" }));
            Assert.That(toggle.IsChecked, Is.Not.True);
            Assert.That(Find<Button>(view, "ToolImageThumbnail").IsEffectivelyVisible, Is.True);
            Click(toggle);
            Assert.That(view.GetVisualDescendants().OfType<AgentToolView>().Count(tool => tool.IsEffectivelyVisible), Is.EqualTo(4));
            Click(toggle);

            var history = Find<ToggleButton>(view, "HistoryToggle");
            Assert.That(VisibleText(view, "Move opening title into the safe area"), Is.False);
            Click(history);
            Assert.That(VisibleText(view, "Move opening title into the safe area"), Is.True);
            Assert.That(view.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible
                && AutomationProperties.GetName(button) == AgentStrings.RevertTurn), Is.True);

            var code = Find<Button>(view, "PART_CopyButton");
            Click(code);
            Assert.That((await window.Clipboard!.TryGetTextAsync())?.ReplaceLineEndings("\n"),
                Is.EqualTo("{\n  \"titleFadeFrames\": 12\n}"));
            Assert.That(((FluentIcon)code.Content!).Icon, Is.EqualTo(Icon.Checkmark));
            await HeadlessTestHelpers.WaitUntilAsync(() => ((FluentIcon)code.Content!).Icon == Icon.Copy);
            Assert.That(ToolTip.GetTip(stats), Does.Contain(1024.ToString("N0")).And.Contain("216").And.Contain("800")
                .And.Not.Contain("GPT-6.2 Sol"));
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(text => Equals(ToolTip.GetTip(text), sender.TimeDetail)
                && text.Text == sender.TimeLabel), Is.True);
            var scroll = Find<ScrollViewer>(view, "TimelineScroll");
            scroll.Offset = default;
            HeadlessTestHelpers.Render(5);
            Assert.That(stats.TranslatePoint(new Point(0, stats.Bounds.Height), scroll)!.Value.Y,
                Is.LessThan(scroll.Viewport.Height));
            Capture(view, window, "phase3-timeline-rich");

            Click(Find<Button>(view, "SentImageThumbnail"));
            preview = await LightboxAsync(window);
            var image = Find<Image>(preview, "FullImage");
            Assert.That(((Bitmap)image.Source!).PixelSize, Is.EqualTo(new PixelSize(720, 432)));
            double fit = image.Width;
            Click(Find<Button>(preview, "ZoomInButton"));
            Assert.That(image.Width, Is.GreaterThan(fit));
            Capture(window, window, "phase3-lightbox");
            Click(Find<Button>(preview, "ZoomOutButton"));
            Assert.That(image.Width, Is.EqualTo(fit).Within(0.5));
            Click(Find<Button>(preview, "ActualSizeButton"));
            Assert.That(image.Width, Is.EqualTo(720 / window.RenderScaling));
            Click(Find<Button>(preview, "FitImageButton"));
            Assert.That(image.Width, Is.EqualTo(fit).Within(0.5));
            Click(Find<Button>(preview, "CopyImagePathButton"));
            Assert.That(await window.Clipboard!.TryGetTextAsync(), Is.EqualTo(framePath));
            Click(Find<Button>(preview, "CopyImageButton"));
            Assert.That((await window.Clipboard!.TryGetBitmapAsync())?.PixelSize, Is.EqualTo(new PixelSize(720, 432)));
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await HeadlessTestHelpers.WaitUntilAsync(() => !HasLightbox(window));

            window.Width = 1440;
            window.Height = 900;
            HeadlessTestHelpers.Render(4);
            Click(Find<Button>(view, "ToolImageThumbnail"));
            preview = await LightboxAsync(window);
            var backdrop = Find<Border>(preview, "Backdrop");
            var card = Find<Border>(preview, "LightboxCard");
            Assert.That(backdrop.Bounds.Size, Is.EqualTo(window.ClientSize));
            Assert.That(card.Bounds.Width, Is.LessThan(window.ClientSize.Width));
            Assert.That(card.Bounds.Height, Is.LessThan(window.ClientSize.Height));
            Assert.That(((Bitmap)Find<Image>(preview, "FullImage").Source!).PixelSize, Is.EqualTo(new PixelSize(720, 432)));
            Assert.That(Find<Button>(preview, "CopyImagePathButton").IsEffectivelyVisible, Is.False);
            Click(Find<Button>(preview, "CloseImageButton"));
            await HeadlessTestHelpers.WaitUntilAsync(() => !HasLightbox(window));

            File.Delete(framePath);
            Click(Find<Button>(view, "SentImageThumbnail"));
            await HeadlessTestHelpers.WaitUntilAsync(() => !sender.Attachments[0].HasPreview);
            Assert.That(view.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "SentFileChip"
                && border.IsEffectivelyVisible && ReferenceEquals(border.DataContext, sender.Attachments[0])), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ToolGroupsKeepFailedImageAndBackgroundCallsVisibleAcrossTurnBoundaries()
    {
        await using var workspace = new AgentTabTestWorkspace();
        string path = Path.Combine(workspace.Root, "frame.png");
        string encoded = CreateFrame(path);
        var transcript = new AgentThreadTimeline();
        var thread = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        try
        {
            Complete("read", "Read", AgentToolStatus.Completed);
            Complete("glob", "Glob", AgentToolStatus.Completed);
            Complete("failed", "Grep", AgentToolStatus.Failed);
            Complete("image", "get_preview", AgentToolStatus.Completed,
                [new AgentToolOutput("image", Data: encoded)], server: "beutl");
            Complete("other", "LS", AgentToolStatus.Completed);
            Complete("background", "Read", AgentToolStatus.Completed, background: true);
            turn = Guid.NewGuid();
            Complete("later", "Read", AgentToolStatus.Completed);
            var group = transcript.Items.OfType<AgentToolGroupItem>().Single();
            Assert.That(group.Tools.Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "read", "glob", "failed" }));
            Assert.That(group.HasFailure, Is.True);
            Assert.That(transcript.Items.OfType<AgentToolItem>().Select(tool => tool.ToolCallId),
                Is.EqualTo(new[] { "image", "other", "background", "later" }));
        }
        finally
        {
            transcript.Clear();
        }

        void Complete(string id, string name, AgentToolStatus status, IReadOnlyList<AgentToolOutput>? outputs = null,
            string? server = null, bool background = false)
        {
            transcript.ApplyEntry(new AgentToolCallStarted(thread, turn, now, id, name, server, IsBackground: background));
            transcript.ApplyEntry(new AgentToolCallCompleted(thread, turn, now, id, status, outputs));
        }
    }

    [AvaloniaTest]
    public async Task ToolGroupsAggregateToolCallsSeparatedByFinishedReasoning()
    {
        await using var workspace = new AgentTabTestWorkspace();
        string path = Path.Combine(workspace.Root, "frame.png");
        string encoded = CreateFrame(path);
        var transcript = new AgentThreadTimeline();
        var thread = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entries = new List<AgentTimelineEvent>();
        try
        {
            Apply(new AgentTurnStarted(thread, turn, now));
            Apply(new AgentReasoningDelta(thread, turn, now.AddSeconds(1), "Initial thought"));
            Complete("exec-1", "execute", now.AddSeconds(2));
            Apply(new AgentReasoningDelta(thread, turn, now.AddSeconds(3), "First bridge thought"));
            Complete("exec-2", "execute", now.AddSeconds(7));
            Apply(new AgentReasoningDelta(thread, turn, now.AddSeconds(8), "Second bridge thought"));
            Complete("exec-3", "execute", now.AddSeconds(10));
            Apply(new AgentReasoningDelta(thread, turn, now.AddSeconds(11), "Before image thought"));
            Complete("exec-image", "execute", now.AddSeconds(15), [new AgentToolOutput("image", Data: encoded)]);
            Apply(new AgentReasoningDelta(thread, turn, now.AddSeconds(16), "Trailing thought"));
            Apply(new AgentTurnCompleted(thread, turn, now.AddSeconds(18)));

            AssertTimelineStructure(transcript);

            // Replaying the persisted transcript must produce the exact same grouping.
            transcript.Restore(entries);
            AssertTimelineStructure(transcript);

            // Unfinished reasoning between two completed tools must not group until it finishes.
            transcript.Clear();
            var liveTurn = Guid.NewGuid();
            transcript.ApplyEntry(new AgentTurnStarted(thread, liveTurn, now));
            transcript.ApplyEntry(new AgentToolCallStarted(thread, liveTurn, now.AddSeconds(1), "first", "execute"));
            transcript.ApplyEntry(new AgentToolCallCompleted(thread, liveTurn, now.AddSeconds(2), "first", AgentToolStatus.Completed));
            transcript.ApplyEntry(new AgentReasoningDelta(thread, liveTurn, now.AddSeconds(3), "Still thinking"));
            transcript.ApplyEntry(new AgentToolCallCompleted(thread, liveTurn, now.AddSeconds(4), "second", AgentToolStatus.Completed));
            Assert.That(transcript.Items.OfType<AgentToolGroupItem>(), Is.Empty);
            transcript.ApplyEntry(new AgentTurnCompleted(thread, liveTurn, now.AddSeconds(5)));
            var liveGroup = transcript.Items.OfType<AgentToolGroupItem>().Single();
            Assert.That(liveGroup.Tools.Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(liveGroup.ThoughtCount, Is.EqualTo(1));
            Assert.That(liveGroup.Label, Is.EqualTo(string.Format(CultureInfo.CurrentCulture, AgentStrings.ToolGroupWithThought,
                string.Format(CultureInfo.CurrentCulture, AgentStrings.ToolGroupTools, 2, 0), 1)));

            // A single tool surrounded by thoughts must stay ungrouped.
            transcript.Clear();
            var singleTurn = Guid.NewGuid();
            transcript.ApplyEntry(new AgentTurnStarted(thread, singleTurn, now));
            transcript.ApplyEntry(new AgentReasoningDelta(thread, singleTurn, now.AddSeconds(1), "Before"));
            transcript.ApplyEntry(new AgentToolCallStarted(thread, singleTurn, now.AddSeconds(2), "only", "execute"));
            transcript.ApplyEntry(new AgentToolCallCompleted(thread, singleTurn, now.AddSeconds(3), "only", AgentToolStatus.Completed));
            transcript.ApplyEntry(new AgentReasoningDelta(thread, singleTurn, now.AddSeconds(4), "After"));
            transcript.ApplyEntry(new AgentTurnCompleted(thread, singleTurn, now.AddSeconds(5)));
            Assert.That(transcript.Items.OfType<AgentToolGroupItem>(), Is.Empty);
            Assert.That(transcript.Items.OfType<AgentToolItem>().Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "only" }));
            Assert.That(transcript.Items.OfType<AgentReasoningItem>().Select(thought => thought.Text),
                Is.EqualTo(new[] { "Before", "After" }));

            // Ungrouping a middle tool with an image output preserves order and regroups the remaining run.
            transcript.Clear();
            var splitTurn = Guid.NewGuid();
            transcript.ApplyEntry(new AgentTurnStarted(thread, splitTurn, now));
            EmitTool(splitTurn, "t1", now.AddSeconds(1));
            transcript.ApplyEntry(new AgentReasoningDelta(thread, splitTurn, now.AddSeconds(2), "r1"));
            EmitTool(splitTurn, "t2", now.AddSeconds(3));
            transcript.ApplyEntry(new AgentReasoningDelta(thread, splitTurn, now.AddSeconds(4), "r2"));
            EmitTool(splitTurn, "t3", now.AddSeconds(5));
            transcript.ApplyEntry(new AgentReasoningDelta(thread, splitTurn, now.AddSeconds(6), "r3"));
            EmitTool(splitTurn, "t4", now.AddSeconds(7));
            Assert.That(transcript.Items.OfType<AgentToolGroupItem>().Single().Tools.Select(tool => tool.ToolCallId),
                Is.EqualTo(new[] { "t1", "t2", "t3", "t4" }));

            transcript.ApplyEntry(new AgentToolCallCompleted(thread, splitTurn, now.AddSeconds(8), "t2",
                AgentToolStatus.Completed, [new AgentToolOutput("image", Data: encoded)]));
            Assert.That(transcript.Items, Has.Count.EqualTo(5));
            Assert.That(((AgentToolItem)transcript.Items[0]).ToolCallId, Is.EqualTo("t1"));
            Assert.That(((AgentReasoningItem)transcript.Items[1]).Text, Is.EqualTo("r1"));
            Assert.That(((AgentToolItem)transcript.Items[2]).ToolCallId, Is.EqualTo("t2"));
            Assert.That(((AgentReasoningItem)transcript.Items[3]).Text, Is.EqualTo("r2"));
            var tailGroup = (AgentToolGroupItem)transcript.Items[4];
            Assert.That(tailGroup.Items.Select(DescribeItem), Is.EqualTo(new[] { "tool:t3", "thought:r3", "tool:t4" }));

            // A late tool that bridges two existing groups merges them and keeps every child linked to the merged group.
            transcript.Clear();
            var mergeTurn = Guid.NewGuid();
            transcript.ApplyEntry(new AgentTurnStarted(thread, mergeTurn, now));
            EmitTool(mergeTurn, "m1", now.AddSeconds(1));
            EmitTool(mergeTurn, "m2", now.AddSeconds(2));
            transcript.ApplyEntry(new AgentToolCallStarted(thread, mergeTurn, now.AddSeconds(3), "slow", "execute"));
            EmitTool(mergeTurn, "m3", now.AddSeconds(4));
            EmitTool(mergeTurn, "m4", now.AddSeconds(5));
            Assert.That(transcript.Items.OfType<AgentToolGroupItem>().Count(), Is.EqualTo(2));
            transcript.ApplyEntry(new AgentToolCallCompleted(thread, mergeTurn, now.AddSeconds(6), "slow", AgentToolStatus.Completed));
            var merged = transcript.Items.OfType<AgentToolGroupItem>().Single();
            Assert.That(merged.Tools.Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "m1", "m2", "slow", "m3", "m4" }));
            Assert.That(merged.Tools.All(tool => ReferenceEquals(tool.Group, merged)), Is.True);

            transcript.ApplyEntry(new AgentToolCallCompleted(thread, mergeTurn, now.AddSeconds(7), "m4",
                AgentToolStatus.Completed, [new AgentToolOutput("image", Data: encoded)]));
            Assert.That(transcript.Items.OfType<AgentToolGroupItem>().Single().Tools.Select(tool => tool.ToolCallId),
                Is.EqualTo(new[] { "m1", "m2", "slow", "m3" }));
            Assert.That(transcript.Items.OfType<AgentToolItem>().Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "m4" }));
        }
        finally
        {
            transcript.Clear();
        }

        void Apply(AgentTimelineEvent entry)
        {
            entries.Add(entry);
            transcript.ApplyEntry(entry);
        }

        void Complete(string id, string name, DateTimeOffset timestamp, IReadOnlyList<AgentToolOutput>? outputs = null)
        {
            Apply(new AgentToolCallStarted(thread, turn, timestamp, id, name));
            Apply(new AgentToolCallCompleted(thread, turn, timestamp, id, AgentToolStatus.Completed, outputs));
        }

        void EmitTool(Guid turnId, string id, DateTimeOffset timestamp)
        {
            transcript.ApplyEntry(new AgentToolCallStarted(thread, turnId, timestamp, id, "execute"));
            transcript.ApplyEntry(new AgentToolCallCompleted(thread, turnId, timestamp, id, AgentToolStatus.Completed));
        }

        static string DescribeItem(AgentTimelineItemViewModel item) => item switch
        {
            AgentToolItem tool => $"tool:{tool.ToolCallId}",
            AgentReasoningItem thought => $"thought:{thought.Text}",
            _ => item.GetType().Name
        };

        static void AssertTimelineStructure(AgentThreadTimeline timeline)
        {
            Assert.That(timeline.Items, Has.Count.EqualTo(5));
            Assert.That(((AgentReasoningItem)timeline.Items[0]).Text, Is.EqualTo("Initial thought"));
            var group = (AgentToolGroupItem)timeline.Items[1];
            Assert.That(group.Items.Select(DescribeItem),
                Is.EqualTo(new[] { "tool:exec-1", "thought:First bridge thought", "tool:exec-2", "thought:Second bridge thought", "tool:exec-3" }));
            Assert.That(group.Tools.Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "exec-1", "exec-2", "exec-3" }));
            Assert.That(group.ThoughtCount, Is.EqualTo(2));
            Assert.That(group.Label, Is.EqualTo(string.Format(CultureInfo.CurrentCulture, AgentStrings.ToolGroupWithThoughts,
                string.Format(CultureInfo.CurrentCulture, AgentStrings.ToolGroupTools, 3, 0), 2)));
            Assert.That(((AgentReasoningItem)timeline.Items[2]).Text, Is.EqualTo("Before image thought"));
            Assert.That(((AgentToolItem)timeline.Items[3]).ToolCallId, Is.EqualTo("exec-image"));
            Assert.That(((AgentReasoningItem)timeline.Items[4]).Text, Is.EqualTo("Trailing thought"));
        }
    }

    [AvaloniaTest]
    public async Task ToolGroupExpansionRendersInterleavedToolsAndThoughtsInOrder()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        workspace.Bridge.Script = (request, _) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "exec-1", "execute", Input: JsonDocument.Parse("{\"step\":1}").RootElement));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddSeconds(1), "exec-1", AgentToolStatus.Completed, [new AgentToolOutput("text", "Step 1 done")]));
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now.AddSeconds(2), "Checking intermediate scene state."));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now.AddSeconds(5), "exec-2", "execute", Input: JsonDocument.Parse("{\"step\":2}").RootElement));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddSeconds(6), "exec-2", AgentToolStatus.Completed, [new AgentToolOutput("text", "Step 2 done")]));
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now.AddSeconds(7), "Verifying timing alignment."));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now.AddSeconds(9), "exec-3", "execute", Input: JsonDocument.Parse("{\"step\":3}").RootElement));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddSeconds(10), "exec-3", AgentToolStatus.Completed, [new AgentToolOutput("text", "Step 3 done")]));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now.AddSeconds(11), "All three steps finished."));
            return Task.CompletedTask;
        };

        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 560, 760);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);

            model.Prompt = "Run the three scene steps.";
            await model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => TryFind<ToggleButton>(view, "TurnStatsToggle") is { IsEffectivelyVisible: true });
            HeadlessTestHelpers.Render(4);

            Click(Find<ToggleButton>(view, "TurnStatsToggle"));
            HeadlessTestHelpers.Render(4);

            var group = model.Timeline.OfType<AgentToolGroupItem>().Single();
            var toggle = Find<ToggleButton>(view, "ToolGroupToggle");
            Assert.That(group.ThoughtCount, Is.EqualTo(2));
            Assert.That(toggle.IsChecked, Is.Not.True);
            Assert.That(view.GetVisualDescendants().OfType<AgentToolView>().Count(tool => tool.IsEffectivelyVisible), Is.EqualTo(0));
            Capture(view, window, "phase3-step-group-collapsed");

            Click(toggle);
            HeadlessTestHelpers.Render(4);
            Assert.That(view.GetVisualDescendants().OfType<AgentToolView>().Count(tool => tool.IsEffectivelyVisible), Is.EqualTo(3));
            var thoughts = view.GetVisualDescendants().OfType<ToggleButton>()
                .Where(button => button.Name == "ThinkingToggle" && button.IsEffectivelyVisible)
                .ToArray();
            Assert.That(thoughts, Has.Length.EqualTo(2));
            Capture(view, window, "phase3-step-group-expanded");

            Click(thoughts[0]);
            HeadlessTestHelpers.Render(4);
            Assert.That(VisibleText(view, "Checking intermediate scene state."), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ScrollButtonAppearsAwayFromTheEndAndReturnsToTheLatestTurn()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        var store = new AgentThreadStore(workspace.Root);
        var thread = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, workspace.GetProviders().Single(), "Opening title review");
        for (int i = 0; i < 8; i++)
        {
            var turn = Guid.NewGuid();
            var then = DateTimeOffset.UtcNow.AddMinutes(i - 8);
            await store.AppendAsync(workspace.ProjectPath, new AgentUserMessage(thread.Id, turn, then,
                i == 0 ? "Review the opening title before export." : "Check the next title placement."));
            if (i == 0)
                await store.AppendAsync(workspace.ProjectPath, new AgentReasoningDelta(thread.Id, turn, then, "Check the safe area."));
            await store.AppendAsync(workspace.ProjectPath, new AgentAssistantMessageCompleted(thread.Id, turn, then,
                "The title stays within the safe area. Its fade begins with the scene and ends before the first cut."));
            await store.AppendAsync(workspace.ProjectPath, new AgentTurnCompleted(thread.Id, turn, then,
                i == 0 ? 131_000 : 2_190, 940, 189, 620, "GPT-6.2 Sol"));
        }

        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 580);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            var scroll = Find<ScrollViewer>(view, "TimelineScroll");
            var button = Find<Button>(view, "ScrollToBottomButton");
            Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height + 48));
            scroll.ScrollToEnd();
            HeadlessTestHelpers.Render(3);
            Assert.That(button.IsEffectivelyVisible, Is.False);
            scroll.Offset = default;
            HeadlessTestHelpers.Render(3);
            Assert.That(button.IsEffectivelyVisible, Is.True);
            Assert.That(VisibleText(view, string.Format(AgentStrings.TurnWorkedFor,
                string.Format(AgentStrings.TurnDurationMinutes, 2, 11))), Is.True);
            Capture(view, window, "phase3-scroll-to-bottom");
            Click(button);
            Assert.That(scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y, Is.LessThan(1));
            Assert.That(button.IsEffectivelyVisible, Is.False);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task TimelineScrollRunsToTheBottomWhileTheComposerStaysPinned()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        var store = new AgentThreadStore(workspace.Root);
        var thread = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, workspace.GetProviders().Single(), "Opening title review");
        for (int i = 0; i < 8; i++)
        {
            var turn = Guid.NewGuid();
            var then = DateTimeOffset.UtcNow.AddMinutes(i - 8);
            await store.AppendAsync(workspace.ProjectPath, new AgentUserMessage(thread.Id, turn, then, "Check the next title placement."));
            await store.AppendAsync(workspace.ProjectPath, new AgentAssistantMessageCompleted(thread.Id, turn, then,
                "The title stays within the safe area. Its fade begins with the scene and ends before the first cut."));
            await store.AppendAsync(workspace.ProjectPath, new AgentTurnCompleted(thread.Id, turn, then, 2_190, 940, 189, 620, "GPT-6.2 Sol"));
        }

        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 580);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            var scroll = Find<ScrollViewer>(view, "TimelineScroll");
            var deck = view.FindControl<Grid>("BottomDeck")!;
            var composerCard = Find<Border>(view, "ComposerCard");
            var contentInset = Find<Border>(view, "TimelineBottomInset");
            var buttonInset = Find<Border>(view, "ScrollToBottomInset");

            // The timeline owns the full height, so its scrollbar reaches the bottom of the tab.
            double scrollBottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), view)!.Value.Y;
            Assert.That(scrollBottom, Is.EqualTo(view.Bounds.Height).Within(1));
            Assert.That(scroll.Viewport.Height, Is.GreaterThan(view.Bounds.Height * 0.8));
            var verticalBar = scroll.GetVisualDescendants().OfType<ScrollBar>()
                .Single(bar => bar.Bounds.Height >= scroll.Bounds.Height * 0.9);
            Assert.That(verticalBar.TranslatePoint(new Point(0, verticalBar.Bounds.Height), view)!.Value.Y,
                Is.EqualTo(view.Bounds.Height).Within(2));
            Assert.That(verticalBar.TranslatePoint(new Point(verticalBar.Bounds.Width, 0), view)!.Value.X,
                Is.EqualTo(view.Bounds.Width).Within(1));

            // The composer is still pinned to the bottom and does not scroll with the timeline.
            double composerBottom = composerCard.TranslatePoint(new Point(0, composerCard.Bounds.Height), view)!.Value.Y;
            Assert.That(composerBottom, Is.EqualTo(view.Bounds.Height - 10).Within(1));

            // Its footprint is reserved both in the scrollable content and for the scroll-to-bottom button.
            Assert.That(contentInset.Bounds.Height, Is.EqualTo(deck.Bounds.Height).Within(1));
            Assert.That(buttonInset.Bounds.Height, Is.EqualTo(deck.Bounds.Height).Within(1));

            scroll.Offset = default;
            HeadlessTestHelpers.Render(3);
            Assert.That(composerCard.TranslatePoint(new Point(0, composerCard.Bounds.Height), view)!.Value.Y,
                Is.EqualTo(composerBottom).Within(0.5));

            // Scrolled to the end, the last message stays above the fixed composer.
            scroll.ScrollToEnd();
            HeadlessTestHelpers.Render(3);
            var lastMessage = view.GetVisualDescendants().OfType<StackPanel>()
                .Last(panel => panel.Classes.Contains("agent-message") && panel.IsEffectivelyVisible);
            Assert.That(lastMessage.TranslatePoint(new Point(0, lastMessage.Bounds.Height), view)!.Value.Y,
                Is.LessThanOrEqualTo(composerCard.TranslatePoint(default, view)!.Value.Y));
            Capture(view, window, "phase3-composer-overlay-scroll");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase("light")]
    [TestCase("dark")]
    [TestCase("dark-border")]
    public async Task TimelineTextDoesNotShowThroughTheBottomDeckCards(string theme)
    {
        // "dark" is the classic dark dock; "dark-border" is the default Dark Border theme the test app installs.
        var variant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var scope = theme == "dark-border" ? ThemeVariantScope.Use(variant) : AgentTabViewTests.UseDockTheme(variant);
        await using var workspace = new AgentTabTestWorkspace();
        var store = new AgentThreadStore(workspace.Root);
        var thread = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, workspace.GetProviders().Single(), "Opening title review");
        for (int i = 0; i < 8; i++)
        {
            var turn = Guid.NewGuid();
            var then = DateTimeOffset.UtcNow.AddMinutes(i - 8);
            await store.AppendAsync(workspace.ProjectPath, new AgentUserMessage(thread.Id, turn, then, "Check the next title placement."));
            await store.AppendAsync(workspace.ProjectPath, new AgentAssistantMessageCompleted(thread.Id, turn, then,
                "The title stays within the safe area. Its fade begins with the scene and ends before the first cut."));
            await store.AppendAsync(workspace.ProjectPath, new AgentTurnCompleted(thread.Id, turn, then, 2_190, 940, 189, 620, "GPT-6.2 Sol"));
        }

        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, variant, 390, 580);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            var scroll = Find<ScrollViewer>(view, "TimelineScroll");
            var composer = view.FindControl<AgentComposerView>("Composer")!;
            // Report every card that lets text through, not just the first.
            using var cards = Assert.EnterMultipleScope();
            AssertCardIsOpaque(window, view, scroll, Find<Border>(view, "ComposerCard"), "composer");
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height - 60));
            Capture(view, window, $"phase3-composer-over-timeline-{theme}");

            // The other deck cards normally appear only during a run; show each one on its own so
            // the same check covers its surface. Permission and question cards replace the composer.
            Control[] others =
            [
                view.GetVisualDescendants().OfType<AgentBackgroundWorkBar>().Single(),
                view.GetVisualDescendants().OfType<AgentQueuedMessagesBar>().Single(),
                view.FindControl<AgentPermissionCard>("ActivePermissionCard")!,
                view.FindControl<AgentQuestionCard>("ActiveQuestionCard")!,
            ];
            foreach (var other in others)
            {
                composer.IsVisible = other is not (AgentPermissionCard or AgentQuestionCard);
                foreach (var control in others)
                    control.IsVisible = control == other;
                HeadlessTestHelpers.Render(5);
                var surface = other.GetVisualDescendants().OfType<AgentCardSurface>().Single();
                AssertCardIsOpaque(window, view, scroll, (Border)surface.Child!, other.GetType().Name);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void AssertCardIsOpaque(Window window, AgentTabView view, ScrollViewer scroll, Border card, string name)
    {
        Assert.That(card.IsEffectivelyVisible && card.Bounds.Height > 0, Is.True, $"{name} should be shown");
        var cardRect = new Rect(card.TranslatePoint(default, window)!.Value, card.Bounds.Size);
        // Stay clear of the rounded corners, where the panel behind the card is meant to show.
        var probe = cardRect.Deflate(new Thickness(card.CornerRadius.TopLeft + 1, 2));

        // At the end of the timeline nothing sits behind the deck.
        scroll.ScrollToEnd();
        window.MouseMove(new Point(5, 5));
        // Let control transitions in the card finish so only the background can differ.
        HeadlessTestHelpers.Render(30);
        int[] clear = ReadPixels(window, probe);

        // Scroll back through the timeline so messages pass behind the card, and check at every step.
        double end = scroll.Extent.Height - scroll.Viewport.Height;
        double overlap = 0;
        int worst = 0;
        for (double delta = 8; delta < scroll.Viewport.Height; delta += 8)
        {
            scroll.Offset = new Vector(0, Math.Max(0, end - delta));
            HeadlessTestHelpers.Render(3);
            overlap = Math.Max(overlap, view.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) && scroll.IsVisualAncestorOf(text))
                .Select(text => new Rect(text.TranslatePoint(default, window)!.Value, text.Bounds.Size).Intersect(probe).Height)
                .DefaultIfEmpty(0).Max());
            int[] covered = ReadPixels(window, probe);
            worst = Math.Max(worst, clear.Zip(covered).Count(pair => Differs(pair.First, pair.Second)));
        }

        Assert.That(overlap, Is.GreaterThanOrEqualTo(Math.Min(12, probe.Height)), $"timeline text should be scrolled behind the {name} card");
        Assert.That(worst, Is.Zero, $"up to {worst} pixels of the {name} card changed while timeline text was behind it");
    }

    // Antialiased control edges can shift by a few levels between frames; text showing through a card
    // changes pixels far more than this.
    private static bool Differs(int a, int b)
    {
        const int tolerance = 8;
        for (int shift = 0; shift < 32; shift += 8)
        {
            if (Math.Abs(((a >> shift) & 0xFF) - ((b >> shift) & 0xFF)) > tolerance)
                return true;
        }
        return false;
    }

    private static int[] ReadPixels(Window window, Rect rect)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var pixels = new List<int>();
        for (int y = (int)Math.Ceiling(rect.Top); y < (int)rect.Bottom; y++)
        {
            for (int x = (int)Math.Ceiling(rect.Left); x < (int)rect.Right; x++)
                pixels.Add(System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address + y * buffer.RowBytes + x * 4));
        }
        return [.. pixels];
    }

    [AvaloniaTest]
    public async Task CopyForkEditAndRetryUseTheSelectedTurnAndSynchronizeTheOriginalThread()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        using var subscriber = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        workspace.Bridge.Script = (request, _) =>
        {
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                request.Text.StartsWith("Check", StringComparison.Ordinal) ? "Checked the opening." : "Updated the title."));
            return Task.CompletedTask;
        };
        model.Prompt = "Check the opening";
        await model.SendAsync();
        var source = model.SelectedThread!;
        await subscriber.InitializeAsync();
        Assert.That(subscriber.SelectedThread!.Id, Is.EqualTo(source.Id));

        string notes = Path.Combine(workspace.Root, "notes.txt");
        await File.WriteAllTextAsync(notes, "Ready to send");
        model.Prompt = "Update the title";
        model.IncludeSceneContext = false;
        await model.AddFilesAsync([notes]);
        await model.SendAsync();
        var messages = model.Timeline.OfType<AgentUserItem>().ToArray();

        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 670);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            Click(Find<Button>(view, "CopyUserMessage", messages[1]));
            Assert.That(await window.Clipboard!.TryGetTextAsync(), Is.EqualTo("Update the title"));

            Click(Find<Button>(view, "ForkAssistantMessage", model.Timeline.OfType<AgentAssistantItem>().First()));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.SelectedThread!.Id != source.Id && model.CanActOnMessages);
            var forkId = model.SelectedThread!.Id;
            Assert.That(model.Timeline.OfType<AgentUserItem>().Select(item => item.Text), Is.EqualTo(new[] { "Check the opening" }));
            Assert.That(model.Timeline.OfType<AgentAssistantItem>().Single().Text, Is.EqualTo("Checked the opening."));
            Assert.That(subscriber.Timeline.OfType<AgentUserItem>(), Has.Exactly(2).Items);

            await model.SelectThreadAsync(source);
            HeadlessTestHelpers.Render(5);
            Click(Find<Button>(view, "EditUserMessage", messages[1]));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.SelectedThread!.Id != source.Id && model.CanActOnMessages);
            Assert.Multiple(() =>
            {
                Assert.That(model.SelectedThread!.Id, Is.Not.EqualTo(forkId));
                Assert.That(model.Prompt, Is.EqualTo("Update the title"));
                Assert.That(model.IncludeSceneContext, Is.False);
                Assert.That(model.Attachments.Single().Path, Is.EqualTo(notes));
                Assert.That(model.Timeline.OfType<AgentUserItem>().Single().Text, Is.EqualTo("Check the opening"));
                Assert.That(subscriber.Timeline.OfType<AgentUserItem>().Last(), Is.SameAs(messages[1]));
            });

            await model.SelectThreadAsync(source);
            HeadlessTestHelpers.Render(5);
            Click(Find<Button>(view, "RetryAssistantMessage", model.Timeline.OfType<AgentAssistantItem>().Last()));
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsRunning && model.CanActOnMessages
                && subscriber.Timeline.OfType<AgentUserItem>().Any(item => item.Text == "Update the title" && item.TurnId != messages[1].TurnId));
            var retried = subscriber.Timeline.OfType<AgentUserItem>().Last();
            Assert.Multiple(() =>
            {
                Assert.That(model.SelectedThread.Id, Is.EqualTo(source.Id));
                Assert.That(subscriber.Timeline.OfType<AgentUserItem>(), Has.Exactly(2).Items);
                Assert.That(model.Timeline.OfType<AgentUserItem>().Last(), Is.SameAs(retried));
                Assert.That(retried.Entry.Attachments!.Single().Path, Is.EqualTo(notes));
                Assert.That(workspace.Submissions.Last().IncludeScene, Is.False);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task LiveThinkingToolCompactionAndRetryPhasesEndWithTimedReasoningAndStats()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var started = new TaskCompletionSource<AgentTurnRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = (request, ct) =>
        {
            started.TrySetResult(request);
            return finish.Task.WaitAsync(ct);
        };
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 580);
        try
        {
            window.Show();
            model.Prompt = "Inspect the cut";
            Task run = model.SendAsync();
            var request = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var (thread, turn, _, _) = request;
            await HeadlessTestHelpers.WaitUntilAsync(() => TryFind<TextBlock>(view, "TurnWorkTime") is { IsEffectivelyVisible: true }
                && view.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Name == "LiveTurnStatus" && text.IsEffectivelyVisible));
            var live = view.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "LiveTurnStatus" && text.IsEffectivelyVisible);
            var workTime = Find<TextBlock>(view, "TurnWorkTime");
            await HeadlessTestHelpers.WaitUntilAsync(() => live.IsEffectivelyVisible && live.Text == AgentStrings.ThinkingInProgress);
            string? workStarted = workTime.Text;
            Assert.That(workStarted, Is.Not.Null.And.Not.Empty);
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now, "Check the title and edit points."));
            await HeadlessTestHelpers.WaitUntilAsync(() => live.Text is { } text && text.Any(char.IsDigit),
                message: "The live thinking phase did not show elapsed time.");
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now.AddSeconds(2), "inspect", "get_scene", "beutl"));
            await HeadlessTestHelpers.WaitUntilAsync(() => live.Text == string.Format(AgentStrings.RunningTool, "get_scene"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddSeconds(2), "inspect", AgentToolStatus.Completed));
            workspace.Bridge.Emit(new AgentTurnPhaseChanged(thread, turn, now, AgentTurnPhase.Compacting));
            await HeadlessTestHelpers.WaitUntilAsync(() => live.Text == AgentStrings.CompactingContext);
            HeadlessTestHelpers.Render(5);
            Assert.That(live.IsEffectivelyVisible, Is.True);
            Assert.That(live.HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Left));
            Capture(view, window, "phase3-compacting");
            workspace.Bridge.Emit(new AgentContextCompacted(thread, turn, now));
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsCompacting);
            Assert.That(model.Timeline.OfType<AgentTimelineDividerItem>().Single().Text, Is.EqualTo(AgentStrings.ContextCompacted));
            workspace.Bridge.Emit(new AgentTurnPhaseChanged(thread, turn, now, AgentTurnPhase.Retrying));
            await HeadlessTestHelpers.WaitUntilAsync(() => live.Text == AgentStrings.RetryingTurn);
            string? retryElapsed = workTime.Text;
            Assert.That(retryElapsed, Is.Not.EqualTo(workStarted));
            await HeadlessTestHelpers.WaitUntilAsync(() => workTime.Text != retryElapsed);
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "The opening cut is ready."));
            workspace.Bridge.Emit(new AgentTurnUsageUpdated(thread, turn, now, 800, 400, 200, "Claude Sonnet"));
            finish.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render(5);
            Assert.That(live.IsEffectivelyVisible, Is.False);
            Click(Find<ToggleButton>(view, "TurnStatsToggle"));
            HeadlessTestHelpers.Render(4);
            Assert.That(Find<ToggleButton>(view, "ThinkingToggle").Content, Is.Not.Null);
            var thought = model.Timeline.OfType<AgentReasoningItem>().Single();
            Assert.That(thought.IsFinished, Is.True);
            Assert.That(thought.Summary, Is.EqualTo(AutomationProperties.GetName(Find<ToggleButton>(view, "ThinkingToggle"))));
            Assert.That(thought.Summary.Any(char.IsDigit), Is.True);
            Assert.That(workTime.Text, Is.EqualTo(model.Timeline.OfType<AgentTurnStatsItem>().Single().WorkTime));
            Assert.That(ToolTip.GetTip(Find<ToggleButton>(view, "TurnStatsToggle")), Does.Contain("800").And.Contain("400").And.Contain("200"));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ModelEffortAndCompactionChangesArePersistedAndCenteredInTheConversation()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace { DefaultModel = "gpt-6.2-sol" };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        using var subscriber = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        workspace.Bridge.Script = (request, _) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            bool secondTurn = request.Text.StartsWith("Summarize", StringComparison.Ordinal);
            if (secondTurn)
                workspace.Bridge.Emit(new AgentContextCompacted(thread, turn, now));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, secondTurn
                ? "The title sits inside the safe area and fades before the first cut."
                : "The opening title and timing notes are ready for review."));
            return Task.CompletedTask;
        };
        model.Prompt = "Review the opening title.";
        await model.SendAsync();
        await subscriber.InitializeAsync();

        await model.OpenModelPickerAsync();
        await model.ChooseModelAsync(model.ModelRows.Single(row => row.Model.Id == "gpt-6.2-astra"));
        await model.ChooseEffortAsync(model.ModelEfforts.Single(row => row.Value == "high"));
        Assert.That(subscriber.Timeline.OfType<AgentTimelineDividerItem>(), Has.Exactly(2).Items);

        model.Prompt = "Summarize the changes.";
        await model.SendAsync();
        var entries = await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id);
        var switches = entries.OfType<AgentThreadModelChanged>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(switches, Has.Length.EqualTo(2));
            Assert.That(switches[0].PreviousModel, Is.EqualTo("gpt-6.2-sol"));
            Assert.That(switches[0].Model, Is.EqualTo("gpt-6.2-astra"));
            Assert.That(switches[1].Effort, Is.EqualTo("high"));
            Assert.That(entries.OfType<AgentContextCompacted>(), Has.Exactly(1).Items);
        });

        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 640);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(6);
            var rows = view.GetVisualDescendants().OfType<Grid>().Where(grid => grid.Name == "TimelineDivider").ToArray();
            var labels = rows.Select(row => row.GetVisualDescendants().OfType<TextBlock>().Single()).ToArray();
            Assert.That(labels.Select(label => label.Text), Is.EqualTo(new[]
            {
                string.Format(AgentStrings.ModelChangeNotice, "6.2 Sol", "6.2 Astra"),
                string.Format(AgentStrings.EffortChangeNotice, AgentStrings.EffortDefault, AgentStrings.EffortHigh),
                AgentStrings.ContextCompacted,
            }));
            Assert.That(rows.Zip(labels).All(pair => pair.Second.TextAlignment == Avalonia.Media.TextAlignment.Center
                && Math.Abs(HeadlessTestHelpers.Center(pair.Second, pair.First).X - pair.First.Bounds.Width / 2) < 16), Is.True);
            Capture(view, window, "phase3-timeline-markers");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task RunningComposerSwitchesItsActionAndBackgroundPopupShowsStopWithoutOverflow()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        string longPath = "/project/reference/" + new string('x', 320) + "/frame-01.png";
        workspace.Bridge.Script = (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "command", "Bash",
                Input: JsonSerializer.SerializeToElement(new { path = longPath }),
                IsBackground: true, BackgroundTitle: "Render reference frames"));
            workspace.Bridge.Emit(new AgentToolCallUpdated(thread, turn, now, "command", AgentToolStatus.Running, longPath));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "review", "Agent",
                Subagent: new AgentSubagent("Reviewer", "Check opening titles")));
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };
        var requested = new List<string>();
        workspace.Bridge.StopTask = (thread, turn, id) =>
        {
            requested.Add(id);
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, DateTimeOffset.UtcNow, id, AgentToolStatus.Cancelled));
            return AgentBackgroundStopResult.Requested;
        };

        bool overlays = TestAppBuilder.HeadlessOptions.OverlayPopups;
        TestAppBuilder.HeadlessOptions.OverlayPopups = true;
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 610);
        try
        {
            window.Show();
            model.Prompt = "Render reference frames and check the opening.";
            Task run = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.HasSubagents && model.BackgroundCommands.HasItems);
            HeadlessTestHelpers.Render(5);
            var composerStop = Find<Button>(view, "StopButton");
            var composerSend = Find<Button>(view, "SendButton");
            Assert.That(composerStop.IsEffectivelyVisible, Is.True);
            Assert.That(composerSend.IsEffectivelyVisible, Is.False);
            Capture(view, window, "phase3-composer-stop");

            model.Prompt = "Check the margin on the end card too.";
            HeadlessTestHelpers.Render(5);
            Assert.That(composerStop.IsEffectivelyVisible, Is.False);
            Assert.That(composerSend.IsEffectivelyVisible, Is.True);

            var bar = view.GetVisualDescendants().OfType<AgentBackgroundWorkBar>().Single();
            var section = Find<ContentControl>(bar, "BackgroundCommandSection");
            var sectionButton = section.GetVisualDescendants().OfType<Button>().Single();
            Click(sectionButton);
            var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(Find<Border>(bar, "WorkCard"))!;
            var body = (Grid)flyout.Content!;
            await HeadlessTestHelpers.WaitUntilAsync(() => flyout.IsOpen && TryFind<Button>(body, "StopWorkTurnButton") is not null);
            var presenter = body.FindAncestorOfType<FlyoutPresenter>()!;
            var expander = body.GetVisualDescendants().OfType<Expander>().First(expander => expander.IsEffectivelyVisible);
            expander.IsExpanded = true;
            HeadlessTestHelpers.Render(5);
            Assert.That(body.ClipToBounds, Is.True);
            Assert.That(ScrollViewer.GetHorizontalScrollBarVisibility(presenter), Is.EqualTo(ScrollBarVisibility.Disabled));
            Assert.That(presenter.GetVisualDescendants().OfType<ScrollViewer>().Select(scroll => scroll.HorizontalScrollBarVisibility),
                Is.Not.Empty.And.All.EqualTo(ScrollBarVisibility.Disabled));
            Assert.That(body.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == longPath
                && text.Bounds.Width <= body.Bounds.Width), Is.True);
            expander.IsExpanded = false;
            var stopTurn = Find<Button>(body, "StopWorkTurnButton");
            var stopTask = body.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Name == "StopBackgroundTaskButton");
            Assert.That(stopTurn.IsEffectivelyVisible && stopTask.IsEffectivelyVisible, Is.True);
            Capture(view, window, "phase3-background-stop");

            Click(stopTask);
            await HeadlessTestHelpers.WaitUntilAsync(() => !stopTask.IsEffectivelyVisible);
            Assert.That(requested, Is.EqualTo(new[] { "command" }));
            Assert.That(model.IsRunning, Is.True);
            Click(stopTurn);
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(model.IsRunning, Is.False);
            flyout.Hide();

            model.Prompt = "Render a second reference.";
            run = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning
                && model.Timeline.OfType<AgentUserItem>().LastOrDefault()?.Text == "Render a second reference.");
            HeadlessTestHelpers.Render(4);
            Click(composerStop);
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(model.IsRunning, Is.False);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlays;
        }
    }

    private static T? TryFind<T>(Control root, string name, object? dataContext = null) where T : Control
        => root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name
            && (dataContext is null || ReferenceEquals(control.DataContext, dataContext)));

    private static T Find<T>(Control root, string name, object? dataContext = null) where T : Control
        => TryFind<T>(root, name, dataContext) ?? throw new InvalidOperationException($"Could not find {name}.");

    private static bool VisibleText(Control root, string value)
        => root.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == value);

    private static void Click(Control control) => HeadlessTestHelpers.Click(control, move: true, ticks: 4);

    private static void Capture(Control control, Window window, string name)
    {
        window.MouseMove(new Point(5, 5));
        HeadlessTestHelpers.Render(8);
        HeadlessCapture.SaveIfRequested(control, string.Empty, name);
    }

    private static bool HasLightbox(Window owner) =>
        OverlayLayer.GetOverlayLayer(owner)?.Children.OfType<AgentImageLightbox>().Any() == true;

    private static async Task<AgentImageLightbox> LightboxAsync(Window owner)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => HasLightbox(owner));
        HeadlessTestHelpers.Render(6);
        return OverlayLayer.GetOverlayLayer(owner)!.Children.OfType<AgentImageLightbox>().Single();
    }

    internal static string CreateFrame(string path)
    {
        using var bitmap = new SKBitmap(720, 432);
        using var canvas = new SKCanvas(bitmap);
        using var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 380),
            [new SKColor(35, 47, 74), new SKColor(130, 101, 115), new SKColor(211, 152, 111)], null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = sky };
        canvas.DrawRect(0, 0, 720, 432, paint);
        paint.Shader = null;
        paint.Color = new SKColor(248, 193, 145);
        canvas.DrawCircle(550, 142, 43, paint);
        using var hills = new SKPathBuilder();
        hills.MoveTo(-10, 330);
        hills.LineTo(128, 204);
        hills.LineTo(269, 344);
        hills.LineTo(408, 242);
        hills.LineTo(578, 336);
        hills.LineTo(729, 253);
        hills.LineTo(729, 450);
        hills.LineTo(-10, 450);
        hills.Close();
        using var silhouette = hills.Detach();
        paint.Color = new SKColor(31, 38, 58);
        canvas.DrawPath(silhouette, paint);
        using var font = new SKFont(SKTypeface.Default, 54);
        paint.Color = new SKColor(252, 236, 221);
        canvas.DrawText("FIELD NOTES", 360, 207, SKTextAlign.Center, font, paint);
        paint.Color = new SKColor(248, 193, 145);
        canvas.DrawRect(335, 226, 50, 2, paint);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] data = png.ToArray();
        File.WriteAllBytes(path, data);
        return Convert.ToBase64String(data);
    }
}
