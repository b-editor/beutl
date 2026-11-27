using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using LiveMarkdown.Avalonia;
using Moq;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentTabPhase5Tests
{
    [AvaloniaTest]
    public async Task ToolDetailsScrollLazilyShimmerUsesAbsoluteCoordinatesAndWideLayoutCentersAt740()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string longOutput = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"line {i:00}: rendering frame preview output"));
        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "cmd1", "Codex command",
                Input: System.Text.Json.JsonSerializer.SerializeToElement("cat long.log")));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddMilliseconds(20), "cmd1", AgentToolStatus.Completed,
                [new("text", longOutput)]));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now.AddMilliseconds(40), "tool1", "render_storyboard", "beutl"));
            await finish.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now.AddMilliseconds(200), "tool1", AgentToolStatus.Completed,
                [new("text", "done")]));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now.AddMilliseconds(240),
                "確認が完了しました。\n詳細は [Beutl ガイド](https://beutl.beditor.net/docs) を確認してください。\n\n複数行の文章でも行間にゆとりを持たせて読みやすくしています。"));
        };

        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var (view, window) = Open(model);
        var originalLaunch = AgentMarkdownRenderer.LaunchUrl;
        string? launchedUrl = null;
        AgentMarkdownRenderer.LaunchUrl = url => launchedUrl = url;
        try
        {
            model.Prompt = "ストーリーボードを確認して";
            _ = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<AgentShimmerTextBlock>(view, "LiveTurnStatus") is { IsLive: true });
            HeadlessTestHelpers.Render(4);

            // 1. Shimmer renders highlight text through a single control-wide opacity mask so Latin + Japanese runs share one sweep.
            var liveStatus = Visible<AgentShimmerTextBlock>(view, "LiveTurnStatus")!;
            Assert.That(liveStatus.Foreground, Is.Not.InstanceOf<LinearGradientBrush>());
            Assert.That(liveStatus.ShimmerMaskBrush, Is.Not.Null);
            Assert.That(liveStatus.ShimmerMaskBrush!.StartPoint.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(liveStatus.ShimmerMaskBrush!.EndPoint.Unit, Is.EqualTo(RelativeUnit.Relative));
            Capture(view, "polish-shimmer-mixed");

            // 2. Collapsed tool details are lazily omitted from the visual tree; expanding constrains height and scrolls.
            Assert.That(view.GetVisualDescendants().OfType<ScrollViewer>().Any(sv => sv.Name == "ToolDetailsScroll" && sv.IsEffectivelyVisible), Is.False);
            var cmdToggle = view.GetVisualDescendants().OfType<ToggleButton>()
                .Single(toggle => toggle.Name == "ToolToggle" && toggle.DataContext is AgentToolItem { ToolCallId: "cmd1" });
            Assert.That(cmdToggle.FindAncestorOfType<AgentToolView>()!.FindControl<ContentControl>("InlineDetailsHost")!.Content, Is.Null);
            Click(cmdToggle);
            HeadlessTestHelpers.Render(4);
            var detailsScroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => sv.Name == "ToolDetailsScroll" && sv.IsEffectivelyVisible);
            Assert.That(detailsScroll.Bounds.Height, Is.LessThanOrEqualTo(260.5));
            Assert.That(detailsScroll.Extent.Height, Is.GreaterThan(detailsScroll.Viewport.Height));
            Capture(view, "polish-tool-scroll");

            finish.TrySetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsRunning);
            HeadlessTestHelpers.Render(4);

            // 3. Work section auto-collapses when finished and lazily unloads its children.
            var workSection = model.DisplayTimeline.OfType<AgentTurnWorkItem>().Single();
            Assert.That(workSection.IsExpanded, Is.False);
            Assert.That(workSection.VisibleEntries, Is.Null);

            // 4. Markdown body line-height is comfortably spaced and links open with confirmation dialog.
            var markdownText = view.GetVisualDescendants().OfType<MarkdownTextBlock>().First();
            Assert.That(markdownText.LineHeight, Is.GreaterThanOrEqualTo(22));
            var renderer = view.GetVisualDescendants().OfType<AgentMarkdownRenderer>().Single();
            renderer.RaiseEvent(new LinkClickedEventArgs(MarkdownTextBlock.LinkClickEvent, renderer, new Uri("https://beutl.beditor.net/docs")));
            await HeadlessTestHelpers.WaitUntilAsync(() => window.GetVisualDescendants().OfType<FluentAvalonia.UI.Controls.FAContentDialog>().Any());
            var dialog = window.GetVisualDescendants().OfType<FluentAvalonia.UI.Controls.FAContentDialog>().Single();
            var primaryButton = dialog.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "PrimaryButton");
            Click(primaryButton);
            await HeadlessTestHelpers.WaitUntilAsync(() => launchedUrl == "https://beutl.beditor.net/docs");
            Assert.That(launchedUrl, Is.EqualTo("https://beutl.beditor.net/docs"));

            // 5. Responsive wide padding (>= 550px) and MaxWidth=740 centered layout on wide panels.
            Assert.That(view.Classes.Contains("wide"), Is.False);
            window.Width = 600;
            HeadlessTestHelpers.Render(4);
            Assert.That(view.Classes.Contains("wide"), Is.True);
            var timelineColumn = view.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "TimelineContentColumn");
            var bottomDeck = view.FindControl<Grid>("BottomDeck")!;
            var composerCard = view.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ComposerCard");
            Assert.That(timelineColumn.TranslatePoint(default, view)!.Value.X, Is.GreaterThanOrEqualTo(28));
            Assert.That(Math.Abs(timelineColumn.Bounds.Width - composerCard.Bounds.Width), Is.LessThanOrEqualTo(0.5));
            Assert.That(Math.Abs(timelineColumn.TranslatePoint(default, view)!.Value.X - composerCard.TranslatePoint(default, view)!.Value.X), Is.LessThanOrEqualTo(0.5));
            Capture(view, "polish-wide-600");

            window.Width = 1040;
            HeadlessTestHelpers.Render(4);
            Assert.That(timelineColumn.Bounds.Width, Is.LessThanOrEqualTo(740.5));
            Assert.That(bottomDeck.Bounds.Width, Is.LessThanOrEqualTo(740.5));
            Assert.That(Math.Abs(timelineColumn.Bounds.Width - composerCard.Bounds.Width), Is.LessThanOrEqualTo(0.5));
            Assert.That(Math.Abs(timelineColumn.TranslatePoint(default, view)!.Value.X - composerCard.TranslatePoint(default, view)!.Value.X), Is.LessThanOrEqualTo(0.5));
            double deckLeft = bottomDeck.TranslatePoint(default, view)!.Value.X;
            double deckRight = view.Bounds.Width - (deckLeft + bottomDeck.Bounds.Width);
            Assert.That(Math.Abs(deckLeft - deckRight), Is.LessThanOrEqualTo(2.0));
            Capture(view, "polish-wide-1040");
        }
        finally
        {
            AgentMarkdownRenderer.LaunchUrl = originalLaunch;
            finish.TrySetResult();
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task AssistantImagesAreLocalValidatedThumbnailsAndOpenTheLightbox()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        string path = Path.Combine(workspace.Root, "review frame.png");
        AgentTabPhase3Tests.CreateFrame(path);
        File.WriteAllText(Path.Combine(workspace.Root, "invalid.png"), "not an image");
        using (var oversized = File.Create(Path.Combine(workspace.Root, "oversized.png")))
            oversized.SetLength(AgentAttachment.MaxImageBytes + 1);
        string absolute = path.Replace('\\', '/');
        string uri = new Uri(path).AbsoluteUri;
        string images = $"""
            ![Absolute](<{absolute}>)
            ![Local file URI]({uri})
            ![Project relative][frame]
            ![Remote](https://example.invalid/frame.png)
            ![UNC](//example.invalid/share/frame.png)
            ![File host](file://example.invalid/share/frame.png)
            ![Data](data:image/png;base64,AA)
            ![Missing](missing.png)
            ![Invalid](invalid.png)
            ![Oversized](oversized.png)

            [frame]: review%20frame.png
            """;
        var previews = AgentMarkdownImages.GetPreviews(images, workspace.Root);
        try
        {
            Assert.That(previews.Select(image => image.Caption), Is.EqualTo(new[] { "Absolute", "Local file URI", "Project relative" }));
            Assert.That(previews.Select(image => image.Path).Distinct().Single(), Does.EndWith("review frame.png"));
        }
        finally
        {
            foreach (var preview in previews)
                preview.Dispose();
        }

        workspace.Bridge.Script = (request, _) =>
        {
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow, """
                ### 冒頭カットの確認

                タイトルを置く余白を右上に確保しました。

                ![参考画像（外部）](https://example.invalid/reference.png)

                ![プレビュー](review%20frame.png)
                """));
            return Task.CompletedTask;
        };
        await model.InitializeAsync();
        var (view, window) = Open(model);
        try
        {
            model.Prompt = "冒頭カットのプレビューを見せて";
            await model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<Button>(view, "AssistantImageThumbnail") is not null);
            var reply = model.DisplayTimeline.OfType<AgentAssistantItem>().Single();
            Assert.That(reply.IsFinalTurnResponse, Is.True);
            Assert.That(reply.Images.Single().Path, Does.EndWith("review frame.png"));
            Assert.That(view.GetVisualDescendants().OfType<MarkdownTextBlock>()
                .Any(block => block.Inlines?.Text?.Contains("参考画像（外部）", StringComparison.Ordinal) == true), Is.True);
            Capture(view, "phase5-markdown-images");

            Click(Find<Button>(view, "AssistantImageThumbnail"));
            await HeadlessTestHelpers.WaitUntilAsync(() => OverlayLayer.GetOverlayLayer(window)?.Children.OfType<AgentImageLightbox>().Any() == true);
            HeadlessTestHelpers.Render(4);
            var lightbox = OverlayLayer.GetOverlayLayer(window)!.Children.OfType<AgentImageLightbox>().Single();
            Assert.That(Find<Border>(lightbox, "Backdrop").Bounds.Size, Is.EqualTo(window.ClientSize));
            Assert.That(Find<Image>(lightbox, "FullImage").Source, Is.Not.Null);
            lightbox.Close();
            var restoredEvents = await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id);
            model.Controller!.Transcript.Restore(restoredEvents);
            Assert.That(reply.Images, Is.Empty);
            Assert.That(model.DisplayTimeline.OfType<AgentAssistantItem>().Single().Images, Has.Count.EqualTo(1));
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task SteerRemainsVisibleWhenWorkIsCollapsedAndAcrossJsonlRestore()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace { SteerOnSend = true };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        var continueWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, DateTimeOffset.UtcNow, "冒頭のカットを確認しています。"));
            await continueWork.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, DateTimeOffset.UtcNow, "配色を保ち、タイトルだけを調整します。"));
            await finish.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, DateTimeOffset.UtcNow, "タイトルの位置を調整しました。"));
        };
        await model.InitializeAsync();
        var (view, window) = Open(model);
        try
        {
            model.Prompt = "冒頭のタイトルを整えて";
            Task turn = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.DisplayTimeline.OfType<AgentTurnWorkItem>().SingleOrDefault()?.Entries.Count == 1);
            var first = model.DisplayTimeline.OfType<AgentTurnWorkItem>().Single();

            model.Prompt = "配色はそのままにして";
            await model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.DisplayTimeline.OfType<AgentTurnWorkItem>().Count() == 2);
            var steer = model.DisplayTimeline.OfType<AgentUserItem>().Last();
            var second = model.DisplayTimeline.OfType<AgentTurnWorkItem>().Last();
            Assert.Multiple(() =>
            {
                Assert.That(model.DisplayTimeline.IndexOf(steer), Is.EqualTo(2));
                Assert.That(steer.TurnId, Is.EqualTo(first.TurnId));
                Assert.That(first.Header.IsRunning, Is.False);
                Assert.That(first.IsExpanded, Is.False);
                Assert.That(second.Header.IsRunning, Is.True);
                Assert.That(second.IsExpanded, Is.True);
                Assert.That(model.ShowStandaloneLiveStatus, Is.False);
            });
            continueWork.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => second.Entries.Count == 1);
            Assert.That(first.IsExpanded, Is.False);
            Assert.That(Visible<AgentShimmerTextBlock>(Find<StackPanel>(view, "TurnWorkBody", second), "LiveTurnStatus"), Is.Not.Null);
            finish.SetResult();
            await turn.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(model.DisplayTimeline.Last(), Is.InstanceOf<AgentAssistantItem>().With.Property(nameof(AgentAssistantItem.IsFinalTurnResponse)).True);
            Assert.That(model.DisplayTimeline.OfType<AgentTurnWorkItem>().All(section => !section.IsExpanded), Is.True);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(control =>
                control.Text == steer.Text && control.IsEffectivelyVisible), Is.True);
            Capture(view, "phase5-steer-collapsed");

            Click(Find<Button>(view, "TurnStatsToggle", first));
            Click(Find<Button>(view, "TurnStatsToggle", second));
            Find<ScrollViewer>(view, "TimelineScroll").Offset = default;
            Capture(view, "phase5-steer-split");
            Click(Find<Button>(view, "TurnStatsToggle", second));
            Assert.That(model.DisplayTimeline.OfType<AgentTurnWorkItem>().Select(section => section.IsExpanded), Is.EqualTo(new[] { true, false }));

            var layout = new JsonObject();
            model.WriteToJson(layout);
            var entries = await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id);
            model.Controller!.Transcript.Restore(entries);
            Assert.That(model.DisplayTimeline.OfType<AgentTurnWorkItem>().Select(section => section.IsExpanded), Is.EqualTo(new[] { true, false }));
            using var reopened = workspace.CreateWorkspace(workspace.Editor);
            reopened.ReadFromJson(JsonNode.Parse(layout.ToJsonString())!.AsObject());
            await reopened.InitializeAsync();
            Assert.That(reopened.DisplayTimeline.OfType<AgentTurnWorkItem>().Select(section => section.IsExpanded), Is.EqualTo(new[] { true, false }));
            Assert.That(reopened.DisplayTimeline.OfType<AgentUserItem>().Last().Text, Is.EqualTo(steer.Text));
        }
        finally
        {
            continueWork.TrySetResult();
            finish.TrySetResult();
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task FailedTurnShowsAuthActionsAndRecheckReadsUpdatedLoginStatus()
    {
        using var ui = new JapaneseDock();
        using var cliDirectory = new TemporaryDirectory();
        string codex = WriteCli(cliDirectory.Path, "codex", "codex-cli 0.165.0", authenticated: false);
        await using var workspace = new AgentTabTestWorkspace(probeVersions: true)
        {
            ProviderDescriptors = [AgentProviderDescriptor.Codex with { Command = codex }]
        };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        const string error = "API error 401: Authentication expired. Please sign in again.";
        workspace.Bridge.Script = (_, _) => throw new InvalidOperationException(error);
        await model.InitializeAsync();
        var (view, window) = Open(model);
        try
        {
            model.Prompt = "冒頭のカットを確認して";
            await model.SendAsync();
            Assert.That(model.Diagnostic?.Issue.Kind, Is.EqualTo(AgentDiagnosticKind.AuthRequired));
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<ToggleButton>(view, "DiagnosticDetailsToggle") is not null);
            var detailToggle = Find<ToggleButton>(view, "DiagnosticDetailsToggle");
            Click(detailToggle);
            Assert.That(Find<SelectableTextBlock>(view, "DiagnosticRawDetail").Text, Is.EqualTo(error));
            Assert.That(((ISolidColorBrush)Find<ContentPresenter>(detailToggle, "PART_ContentPresenter").Background!).Color,
                Is.EqualTo(Colors.Transparent));
            Click(Find<Button>(view, "CopyDiagnosticCommand"));
            Assert.That(await window.Clipboard!.TryGetTextAsync(), Is.EqualTo("codex login"));
            Assert.That(Find<FluentIcon>(view, "CopyDiagnosticIcon").Icon, Is.EqualTo(Icon.Checkmark));
            Click(Find<Button>(view, "OpenDiagnosticSettings"));
            Assert.That(workspace.SettingsOwner, Is.SameAs(window));
            Capture(view, "phase5-diagnostic-auth");

            await model.RecheckDiagnosticAsync();
            Assert.That(model.Diagnostic?.Issue.Kind, Is.EqualTo(AgentDiagnosticKind.AuthRequired));
            WriteCli(cliDirectory.Path, "codex", "codex-cli 0.165.0", authenticated: true);
            Click(Find<Button>(view, "RecheckDiagnostic"));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.Diagnostic is null);
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task OutdatedCliHasARecoveryCardAndSettingsShowVersionsAndNotifications()
    {
        using var ui = new JapaneseDock();
        using var cliDirectory = new TemporaryDirectory();
        string claude = WriteCli(cliDirectory.Path, "claude", "2.0.9 (Claude Code)");
        string codex = WriteCli(cliDirectory.Path, "codex", "codex-cli 0.165.0");
        await using var workspace = new AgentTabTestWorkspace(probeVersions: true)
        {
            ProviderDescriptors = [AgentProviderDescriptor.ClaudeCode with { Command = claude },
                AgentProviderDescriptor.Codex with { Command = codex }]
        };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var (view, window) = Open(model);
        try
        {
            Assert.That(model.Diagnostic?.Issue.Kind, Is.EqualTo(AgentDiagnosticKind.OutdatedCli));
            Assert.That(model.Diagnostic?.Issue.CliVersion, Is.EqualTo("2.0.9"));
            Capture(view, "phase5-diagnostic-outdated");
            Click(Find<Button>(view, "DismissDiagnostic"));
            Assert.That(model.Diagnostic, Is.Null);
        }
        finally
        {
            Close(window);
        }

        var config = new AiAgentConfig
        {
            ClaudeCodeExecutablePath = { Value = claude },
            CodexExecutablePath = { Value = codex }
        };
        using var settingsModel = new AiAgentSettingsPageViewModel(config);
        await settingsModel.RuntimeSettings.PendingProbe;
        var page = new AiAgentSettingsPage { DataContext = settingsModel };
        var settingsWindow = new Window { Content = page, Width = 820, Height = 840, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            settingsWindow.Show();
            HeadlessTestHelpers.Render(5);
            Assert.That(settingsModel.RuntimeSettings.ClaudeCodeStatus.Value, Does.Contain("2.0.9").And.Contain("2.1.0"));
            Assert.That(settingsModel.RuntimeSettings.CodexStatus.Value, Does.Contain("0.165.0"));
            var notifications = page.FindControl<ToggleSwitch>("EnableAgentNotifications")!;
            var sound = page.FindControl<ToggleSwitch>("EnableAgentNotificationSound")!;
            Assert.That(notifications.IsChecked, Is.True);
            Assert.That(sound.IsChecked, Is.True);
            Capture(page, "phase5-settings-page");
            Click(sound);
            Click(notifications);
            Assert.That(sound.IsEffectivelyEnabled, Is.False);
            Assert.That(config.EnableNotificationSound.Value, Is.False);
            Assert.That(config.EnableNotifications.Value, Is.False);
        }
        finally
        {
            Close(settingsWindow);
        }
    }

    [AvaloniaTest]
    public async Task SharedControllerNotifiesOnceAndSelectedTabsAcknowledgeActivity()
    {
        var notifications = new RecordingNotifications();
        var sound = new RecordingSound();
        var editor = new Mock<IEditorContext>();
        editor.Setup(context => context.GetService(typeof(INotificationService))).Returns(notifications);
        await using var workspace = new AgentTabTestWorkspace(sound)
        {
            Editor = editor.Object,
            DefaultPermissionMode = AgentPermissionMode.Supervised
        };
        using var first = workspace.CreateWorkspace(workspace.Editor);
        using var mirror = workspace.CreateWorkspace(workspace.Editor);
        await first.InitializeAsync();
        await mirror.InitializeAsync();
        var allowApproval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (request, ct) =>
        {
            await allowApproval.Task.WaitAsync(ct);
            await workspace.Bridge.AskPermissionAsync(request, "Edit", null, ct);
            await workspace.Bridge.AskQuestionAsync(request, [new AgentQuestion("style", "Which style?")], ct);
        };
        first.Prompt = "Review the cut";
        Task turn = first.SendAsync();
        try
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => first.IsRunning);
            await mirror.SelectThreadAsync(first.SelectedThread!);
            allowApproval.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => mirror.HasActivePermission);
            Assert.Multiple(() =>
            {
                Assert.That(first.Header.Value, Is.EqualTo(string.Format(AgentStrings.AttentionTabHeader, AgentStrings.Title)));
                Assert.That(mirror.Header.Value, Is.EqualTo(first.Header.Value));
                Assert.That(notifications.Items.Select(item => item.Type), Is.EqualTo(new[] { NotificationType.Warning }));
                Assert.That(notifications.Items[0].Title, Is.EqualTo(AgentStrings.NotificationApproval));
                Assert.That(sound.Count, Is.EqualTo(1));
            });

            first.IsSelected.Value = true;
            Assert.That(first.Header.Value, Is.EqualTo(AgentStrings.Title));
            first.ActivePermission!.Respond(AgentPermissionDecision.AllowOnce);
            await HeadlessTestHelpers.WaitUntilAsync(() => mirror.ShowQuestionCard);
            Assert.That(notifications.Items, Has.Count.EqualTo(1));
            Assert.That(sound.Count, Is.EqualTo(1));
            first.IsSelected.Value = false;
            first.ActiveQuestion!.Skip();
            await turn.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(first.Header.Value, Is.EqualTo(string.Format(AgentStrings.UnreadTabHeader, AgentStrings.Title)));
                Assert.That(notifications.Items.Select(item => item.Type), Is.EqualTo(new[] { NotificationType.Warning, NotificationType.Information }));
                Assert.That(sound.Count, Is.EqualTo(2));
            });

            workspace.EnableNotificationSound = false;
            workspace.Bridge.Script = (_, _) => throw new InvalidOperationException("Provider connection closed.");
            first.Prompt = "Retry review";
            await first.SendAsync();
            Assert.That(notifications.Items.Select(item => item.Type),
                Is.EqualTo(new[] { NotificationType.Warning, NotificationType.Information, NotificationType.Error }));
            Assert.That(sound.Count, Is.EqualTo(2));

            workspace.EnableNotifications = false;
            workspace.EnableNotificationSound = true;
            first.Prompt = "Retry again";
            await first.SendAsync();
            Assert.That(notifications.Items, Has.Count.EqualTo(3));
            Assert.That(sound.Count, Is.EqualTo(2));
        }
        finally
        {
            allowApproval.TrySetResult();
        }
    }

    [AvaloniaTest]
    public async Task ShortcutsCyclePermissionsFocusPickersAndRespectPreedit()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace { DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        var existing = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, workspace.ProviderDescriptors[0],
            "カットの確認", AgentPermissionMode.Supervised);
        await model.InitializeAsync();
        var (view, window) = Open(model);
        try
        {
            var prompt = Find<TextBox>(view, "PromptBox");
            prompt.Focus();
            var presenter = prompt.GetVisualDescendants().OfType<TextPresenter>().First();
            presenter.PreeditText = "編集中";
            Press(window, PhysicalKey.N, CommandModifier);
            Assert.That(model.SelectedThread?.Id, Is.EqualTo(existing.Id));
            presenter.PreeditText = null;
            prompt.Focus();

            foreach (var mode in new[] { AgentPermissionMode.AutoAcceptEdits, AgentPermissionMode.Auto,
                         AgentPermissionMode.FullAccess, AgentPermissionMode.Supervised })
            {
                Press(window, PhysicalKey.Tab, RawInputModifiers.Shift);
                await HeadlessTestHelpers.WaitUntilAsync(() => model.CanConfigure && model.SelectedPermissionMode.Mode == mode);
            }

            var switcher = Find<Button>(view, "ThreadSwitcherButton");
            var threadFlyout = (Flyout)switcher.Flyout!;
            Press(window, PhysicalKey.K, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => threadFlyout.IsOpen && Find<TextBox>((Control)threadFlyout.Content!, "ThreadSearchBox").IsFocused);
            Press(window, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.That(model.SelectedPermissionMode.Mode, Is.EqualTo(AgentPermissionMode.Supervised));
            Press(window, PhysicalKey.Escape);
            await HeadlessTestHelpers.WaitUntilAsync(() => !threadFlyout.IsOpen);

            var modelFlyout = (Flyout)Find<Button>(view, "ModelButton").Flyout!;
            prompt.Focus();
            Press(window, PhysicalKey.M, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => modelFlyout.IsOpen && Find<TextBox>((Control)modelFlyout.Content!, "ModelSearchBox").IsFocused);
            Press(window, PhysicalKey.Escape);
            await HeadlessTestHelpers.WaitUntilAsync(() => !modelFlyout.IsOpen);

            prompt.Focus();
            Press(window, PhysicalKey.N, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.SelectedThread is null && prompt.IsFocused);
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task EscapeDismissesOpenPopupsBeforeArmingAndRequiresTwoDistinctPresses()
    {
        using var ui = new JapaneseDock();
        var notifications = new RecordingNotifications();
        var editor = new Mock<IEditorContext>();
        editor.Setup(context => context.GetService(typeof(INotificationService))).Returns(notifications);
        await using var workspace = new AgentTabTestWorkspace
        {
            Editor = editor.Object,
            DefaultPermissionMode = AgentPermissionMode.Supervised
        };
        using var background = workspace.CreateWorkspace(workspace.Editor);
        using var foreground = workspace.CreateWorkspace(workspace.Editor);
        await background.InitializeAsync();
        await foreground.InitializeAsync();
        foreground.IsSelected.Value = true;
        workspace.Bridge.Script = async (request, ct) =>
        {
            if (request.Text == "別のカットを更新して")
            {
                await workspace.Bridge.AskPermissionAsync(request, "Edit", null, ct);
                return;
            }
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(request.ThreadId, request.TurnId,
                DateTimeOffset.UtcNow, "冒頭のカットを確認しています。"));
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };

        var (view, window) = Open(foreground);
        try
        {
            background.Prompt = "別のカットを更新して";
            Task backgroundTurn = background.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => background.HasActivePermission);
            foreground.Prompt = "冒頭のカットを確認して";
            Task foregroundTurn = foreground.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => foreground.IsRunning && foreground.DisplayTimeline.OfType<AgentTurnWorkItem>().Any());
            Assert.That(Visible<Ellipse>(view, "BackgroundThreadActivityDot"), Is.Not.Null);
            Assert.That(notifications.Items.Single().Type, Is.EqualTo(NotificationType.Warning));

            var prompt = Find<TextBox>(view, "PromptBox");
            var flyout = Find<Button>(view, "ThreadSwitcherButton").Flyout!;
            prompt.Focus();
            Press(window, PhysicalKey.K, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => flyout.IsOpen);
            Press(window, PhysicalKey.Escape);
            await HeadlessTestHelpers.WaitUntilAsync(() => !flyout.IsOpen);
            Assert.That(foreground.ShowEscapeStopHint, Is.False);

            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            prompt.Focus();
            prompt.Text = "/";
            prompt.CaretIndex = 1;
            await HeadlessTestHelpers.WaitUntilAsync(() => composer.HasOpenSuggestions);
            Press(window, PhysicalKey.Escape);
            Assert.That(composer.HasOpenSuggestions, Is.False);
            Assert.That(foreground.ShowEscapeStopHint, Is.False);
            prompt.Text = null;

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.That(foreground.CanStop, Is.True);
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<TextBlock>(view, "EscapeStopHint") is not null);
            Capture(view, "phase5-double-esc-hint");

            await HeadlessTestHelpers.WaitUntilAsync(() => !foreground.ShowEscapeStopHint);
            Assert.That(foreground.CanStop, Is.True);
            // A render between presses can consume the wall-clock confirmation window on a busy host.
            for (int press = 0; press < 2; press++)
            {
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            }
            await foregroundTurn.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(foreground.IsRunning || foreground.ShowEscapeStopHint, Is.False);
            background.ActivePermission!.Respond(AgentPermissionDecision.RejectOnce);
            await backgroundTurn.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(foreground.HasBackgroundThreadActivity, Is.True);
            await foreground.SelectThreadAsync(background.SelectedThread!);
            Assert.That(foreground.HasBackgroundThreadActivity, Is.False);
        }
        finally
        {
            Close(window);
        }
    }

    private static RawInputModifiers CommandModifier
        => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        HeadlessTestHelpers.Render(2);
    }

    private static (AgentTabView View, Window Window) Open(AgentTabViewModel model)
    {
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 610);
        window.Show();
        HeadlessTestHelpers.Render(5);
        return (view, window);
    }

    private static void Close(Window window)
    {
        window.Close();
        HeadlessTestHelpers.Settle();
    }

    private static T Find<T>(Control root, string name, object? dataContext = null) where T : Control
        => root.GetVisualDescendants().OfType<T>().First(control =>
            control.Name == name && (dataContext is null || ReferenceEquals(control.DataContext, dataContext)));

    private static T? Visible<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name && control.IsEffectivelyVisible);

    private static void Click(Control control) => HeadlessTestHelpers.Click(control, move: true, ticks: 4);

    private static void Capture(Control control, string name)
    {
        HeadlessTestHelpers.Render(6);
        HeadlessCapture.SaveIfRequested(control, string.Empty, name);
    }

    private static string WriteCli(string directory, string name, string version, bool authenticated = true)
    {
        string path = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".cmd" : name);
        int authResult = authenticated ? 0 : 1;
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, $"@echo off\r\nif \"%1\"==\"--version\" goto version\r\nexit /b {authResult}\r\n:version\r\necho {version}\r\n");
        }
        else
        {
            File.WriteAllText(path, $"#!/bin/sh\nif [ \"$1\" = --version ]; then printf '%s\\n' '{version}'; exit 0; fi\nexit {authResult}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private sealed class JapaneseDock : IDisposable
    {
        private readonly IDisposable _theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        private readonly bool _popups = TestAppBuilder.HeadlessOptions.OverlayPopups;

        public JapaneseDock()
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            TestAppBuilder.HeadlessOptions.OverlayPopups = true;
        }

        public void Dispose()
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = _popups;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
            _theme.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("beutl-agent-phase5-");
        public string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<Notification> Items { get; } = [];
        public void Show(Notification notification) => Items.Add(notification);
    }

    private sealed class RecordingSound : IAgentNotificationSoundPlayer
    {
        public int Count { get; private set; }

        public Task PlayAsync(CancellationToken cancellationToken)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
