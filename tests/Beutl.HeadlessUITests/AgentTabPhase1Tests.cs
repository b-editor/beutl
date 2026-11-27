using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AgentTabPhase1Tests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [AvaloniaTest]
    public async Task TwoScenesShareOneLiveThreadAndDrainWithTheSubmittingEditor()
    {
        await using var workspace = new AgentTabTestWorkspace();
        var opening = SceneEditor("Opening");
        var credits = SceneEditor("Credits");
        using var first = workspace.CreateWorkspace(opening);
        using var second = workspace.CreateWorkspace(credits);
        await Task.WhenAll(first.InitializeAsync(), second.InitializeAsync());
        var requestInteractions = Gate();
        var releaseFirst = Gate();
        AgentPermissionDecision? decision = null;
        IReadOnlyDictionary<string, string[]>? answers = null;
        workspace.Bridge.Script = async (request, ct) =>
        {
            if (request.Text != "Review the opening")
                return;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentAssistantTextDelta(request.ThreadId, request.TurnId, now, "I found the opening title.", "reply"));
            workspace.Bridge.Emit(new AgentToolCallStarted(request.ThreadId, request.TurnId, now, "render", "render_preview",
                IsBackground: true, BackgroundTitle: "Preview frames"));
            await requestInteractions.Task.WaitAsync(ct);
            var permission = workspace.Bridge.AskPermissionAsync(request, "Update the title", "beutl", ct);
            var question = workspace.Bridge.AskQuestionAsync(request, [new AgentQuestion("title", "What title should I use?")], ct);
            decision = await permission;
            answers = await question;
            await releaseFirst.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentToolCallCompleted(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                "render", AgentToolStatus.Completed));
        };

        first.Prompt = "Review the opening";
        Task run = first.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => first.IsRunning && first.BackgroundCommands.HasItems);
        await second.SelectThreadAsync(first.SelectedThread!);
        Assert.That(second.Controller, Is.SameAs(first.Controller));
        Assert.That(second.IsRunning, Is.True);
        Assert.That(second.Timeline.OfType<AgentAssistantItem>().Single(), Is.SameAs(first.Timeline.OfType<AgentAssistantItem>().Single()));
        Assert.That(second.BackgroundCommands, Is.SameAs(first.BackgroundCommands));
        Assert.That(second.SceneContextTooltip, Does.StartWith("Credits"));

        second.IncludeSceneContext = false;
        await second.CaptureFrameAsync();
        second.Prompt = "Check the closing credits";
        await second.SendAsync();
        first.Prompt = "Discard this follow-up";
        await first.SendAsync();
        Assert.That(second.QueuedMessages, Is.SameAs(first.QueuedMessages));
        first.MoveQueuedMessage(second.QueuedMessages[1], -1);
        Assert.That(second.QueuedMessages[0].Text, Is.EqualTo("Discard this follow-up"));
        second.RemoveQueuedMessage(first.QueuedMessages[0]);
        Assert.That(first.QueuedMessages.Single().Editor, Is.SameAs(credits));

        requestInteractions.SetResult();
        await HeadlessTestHelpers.WaitUntilAsync(() => first.HasActivePermission && second.HasActiveQuestion);
        Assert.That(second.ActivePermission, Is.SameAs(first.ActivePermission));
        Assert.That(second.ActiveQuestion, Is.SameAs(first.ActiveQuestion));
        first.ActivePermission!.Respond(AgentPermissionDecision.AllowOnce);
        await HeadlessTestHelpers.WaitUntilAsync(() => second.ShowQuestionCard);
        second.ActiveQuestion!.CurrentQuestion!.Text = "City at night";
        second.ActiveQuestion.Advance();
        await HeadlessTestHelpers.WaitUntilAsync(() => first.ShowComposer && second.ShowComposer);
        releaseFirst.SetResult();
        await run.WaitAsync(s_timeout);

        Assert.That(decision, Is.EqualTo(AgentPermissionDecision.AllowOnce));
        Assert.That(answers!["title"], Is.EqualTo(new[] { "City at night" }));
        Assert.That(second.Timeline.OfType<AgentQuestionItem>().Single(), Is.SameAs(first.Timeline.OfType<AgentQuestionItem>().Single()));
        Assert.That(second.Timeline.OfType<AgentUserItem>().Select(item => item.Text), Is.EqualTo(new[] { "Review the opening", "Check the closing credits" }));
        Assert.That(second.IsRunning, Is.False);
        Assert.That(second.QueuedMessages, Is.Empty);
        var submission = workspace.Submissions.Last();
        Assert.That(submission.Editor, Is.SameAs(credits));
        Assert.That(submission.IncludeScene, Is.False);
        Assert.That(submission.Attachments.Single().MediaType, Is.EqualTo("image/png"));
    }

    [AvaloniaTest]
    public async Task ThreadPickerSwitchesWhileTwoTurnsRunAndTracksBackgroundAttention()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var ask = Gate();
        var releaseSecond = Gate();
        workspace.Bridge.Script = async (request, ct) =>
        {
            workspace.Bridge.Emit(new AgentAssistantTextDelta(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                request.Text.StartsWith("Opening", StringComparison.Ordinal) ? "I’m checking the opening shots." : "I’m reviewing the last frame.", "reply"));
            if (request.Text.StartsWith("Opening", StringComparison.Ordinal))
            {
                await ask.Task.WaitAsync(ct);
                await workspace.Bridge.AskPermissionAsync(request, "Inspect the opening", "beutl", ct);
            }
            else
            {
                await releaseSecond.Task.WaitAsync(ct);
            }
        };

        bool overlays = TestAppBuilder.HeadlessOptions.OverlayPopups;
        TestAppBuilder.HeadlessOptions.OverlayPopups = true;
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var bar = view.GetVisualDescendants().OfType<AgentThreadBar>().Single();
            var switcher = bar.FindControl<Button>("ThreadSwitcherButton")!;
            var picker = (Flyout)switcher.Flyout!;
            model.Prompt = "Opening title pass";
            Task firstRun = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning && model.Timeline.OfType<AgentAssistantItem>().Any());
            var first = model.SelectedThread!;
            Click(Action(bar, AgentStrings.NewThread));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.SelectedThread is null && model.CanManage);
            model.Prompt = "Review the end card";
            Task secondRun = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning && model.Timeline.OfType<AgentAssistantItem>().Any());
            var second = model.SelectedThread!;
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            Assert.That(firstRun.IsCompleted, Is.False);
            Assert.That(secondRun.IsCompleted, Is.False);

            Click(switcher);
            Assert.That(picker.IsOpen, Is.True);
            var matches = bar.FindControl<ItemsControl>("ThreadMatches")!;
            var previousRow = Action(matches, first.Title);
            await HeadlessTestHelpers.WaitUntilAsync(() => previousRow.GetVisualDescendants().OfType<Control>()
                .Any(control => control.Name == "ThreadRunningIndicator" && control.IsEffectivelyVisible));
            Assert.That(model.FilteredThreads.Single(row => row.Thread.Id == first.Id).CanDelete, Is.False);
            Capture(view, window, "phase1-thread-switcher-running");

            ask.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => Action(matches, first.Title).GetVisualDescendants().OfType<Control>()
                .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == AgentStrings.ThreadNeedsAttention));
            Click(Action(matches, first.Title));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.SelectedThread?.Id == first.Id && model.HasActivePermission);
            model.ActivePermission!.Respond(AgentPermissionDecision.AllowOnce);
            await firstRun.WaitAsync(s_timeout);
            await model.SelectThreadAsync(second);
            Assert.That(model.IsRunning, Is.True);
            releaseSecond.SetResult();
            await secondRun.WaitAsync(s_timeout);
            Assert.That(model.FilteredThreads.All(row => !row.IsRunning && !row.NeedsAttention), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlays;
        }
    }

    [AvaloniaTest]
    public async Task QueueDockEditsReordersSteersAndDrainsWithAnEditableComposer()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var release = Gate();
        workspace.Bridge.Script = async (request, ct) =>
        {
            if (request.Text == "Review the opening cut")
            {
                workspace.Bridge.Emit(new AgentAssistantTextDelta(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                    "I’m checking the timing and readability of the opening title.", "reply"));
                await release.Task.WaitAsync(ct);
            }
        };
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            var send = composer.FindControl<Button>("SendButton")!;
            var stop = composer.FindControl<Button>("StopButton")!;
            model.Prompt = "Review the opening cut";
            Task run = model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning && model.Timeline.OfType<AgentAssistantItem>().Any());
            Assert.That(prompt.IsReadOnly, Is.False);
            Assert.That(stop.IsEffectivelyVisible, Is.True);
            Assert.That(send.IsEffectivelyVisible, Is.False);

            composer.FocusPrompt();
            window.KeyTextInput("Keep the title above the lower-third throughout the opening sequence.");
            Assert.That(stop.IsEffectivelyVisible, Is.False);
            Assert.That(send.IsEffectivelyVisible, Is.True);
            Assert.That(AutomationProperties.GetName(send), Is.EqualTo(AgentStrings.QueueMessage));
            Press(window, PhysicalKey.Enter);
            composer.FocusPrompt();
            window.KeyTextInput("Check the last scene too.");
            HeadlessTestHelpers.Render(3);
            Click(send);
            Assert.That(model.QueuedMessages, Has.Count.EqualTo(2));
            var queue = view.GetVisualDescendants().OfType<AgentQueuedMessagesBar>().Single();
            Assert.That(queue.TranslatePoint(default, window)!.Value.Y, Is.LessThan(composer.TranslatePoint(default, window)!.Value.Y));
            Assert.That(stop.IsEffectivelyVisible, Is.True);
            model.Prompt = "Keep the lower third clear.";
            Capture(view, window, "phase1-queue-dock");
            model.Prompt = null;
            HeadlessTestHelpers.Render(4);

            var earlier = model.QueuedMessages[0];
            var later = model.QueuedMessages[1];
            var lateHandle = Action(queue, AgentStrings.ReorderQueuedMessage, later);
            var earlyHandle = Action(queue, AgentStrings.ReorderQueuedMessage, earlier);
            Assert.That(HeadlessTestHelpers.Center(lateHandle, queue).X,
                Is.LessThan(HeadlessTestHelpers.Center(Action(queue, AgentStrings.EditQueuedMessage, later), queue).X));
            var dragStart = HeadlessTestHelpers.Center(lateHandle, window);
            var dragTarget = HeadlessTestHelpers.Center(earlyHandle, window);
            window.MouseMove(dragStart);
            window.MouseDown(dragStart, MouseButton.Left);
            window.MouseMove(dragTarget, RawInputModifiers.LeftMouseButton);
            window.MouseUp(dragTarget, MouseButton.Left);
            HeadlessTestHelpers.Render(4);
            Assert.That(model.QueuedMessages[0], Is.SameAs(later));
            Action(queue, AgentStrings.ReorderQueuedMessage, later).Focus();
            Press(window, PhysicalKey.ArrowDown);
            Assert.That(model.QueuedMessages[0], Is.SameAs(earlier));
            Click(Action(queue, AgentStrings.EditQueuedMessage, earlier));
            await HeadlessTestHelpers.WaitUntilAsync(() => prompt.Text == earlier.Text);
            model.Prompt = "Keep the title inside the safe area.";
            await model.SendAsync();
            HeadlessTestHelpers.Render(4);
            Assert.That(model.QueuedMessages.Select(message => message.Text),
                Is.EqualTo(new[] { "Check the last scene too.", "Keep the title inside the safe area." }));
            Click(Action(queue, AgentStrings.RemoveQueuedMessage, later));
            Assert.That(model.QueuedMessages, Has.Count.EqualTo(1));

            model.Prompt = "Verify the final cut.";
            await model.SendAsync();
            HeadlessTestHelpers.Render(4);
            Click(Action(queue, AgentStrings.SteerNow, model.QueuedMessages[0]));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.QueuedMessages.Count == 1);
            Assert.That(model.QueuedMessages.Single().Text, Is.EqualTo("Verify the final cut."));
            composer.FocusPrompt();
            window.KeyTextInput("Keep the same font.");
            var command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            Press(window, PhysicalKey.Enter, command);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.Timeline.OfType<AgentUserItem>().Any(item => item.Text == "Keep the same font."));

            workspace.SteerOnSend = true;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                AutomationProperties.GetName(send) == AgentStrings.SteerNow);
            composer.FocusPrompt();
            window.KeyTextInput("Keep the same color.");
            Assert.That(stop.IsEffectivelyVisible, Is.False);
            Assert.That(send.IsEffectivelyVisible, Is.True);
            string shortcut = OperatingSystem.IsMacOS() ? "⌘+Enter" : "Ctrl+Enter";
            Assert.That(ToolTip.GetTip(send), Does.Contain(string.Format(AgentStrings.QueuePromptShortcut, shortcut)));
            Press(window, PhysicalKey.Enter);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.Timeline.OfType<AgentUserItem>().Any(item => item.Text == "Keep the same color."));
            composer.FocusPrompt();
            window.KeyTextInput("Check the export.");
            Press(window, PhysicalKey.Enter, command);
            Assert.That(model.QueuedMessages.Select(message => message.Text), Is.EqualTo(new[] { "Verify the final cut.", "Check the export." }));
            release.SetResult();
            await run.WaitAsync(s_timeout);

            var users = model.Timeline.OfType<AgentUserItem>().ToArray();
            Assert.That(users.Select(item => item.Text), Is.EqualTo(new[]
            {
                "Review the opening cut", "Keep the title inside the safe area.", "Keep the same font.", "Keep the same color.",
                "Verify the final cut.", "Check the export."
            }));
            Assert.That(users.Take(4).Select(item => item.TurnId), Is.All.EqualTo(users[0].TurnId));
            Assert.That(users.Skip(4).Select(item => item.TurnId), Is.Unique.And.All.Not.EqualTo(users[0].TurnId));
            Assert.That(model.QueuedMessages, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ApprovalCardNavigatesTwoPendingRequestsAndReturnsToTheComposer()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var release = Gate();
        AgentPermissionDecision[]? decisions = null;
        workspace.Bridge.Script = async (request, ct) =>
        {
            workspace.Bridge.Emit(new AgentAssistantTextDelta(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                "I found two edits to review before applying the opening treatment.", "reply"));
            decisions = await Task.WhenAll(
                workspace.Bridge.AskPermissionAsync(request, "set_title_style", "beutl", ct, "Increase contrast on the opening title."),
                workspace.Bridge.AskPermissionAsync(request, "set_element_timing", "beutl", ct, "Hold the title for 3 seconds."));
            await release.Task.WaitAsync(ct);
        };
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            model.Prompt = "Refine the opening title";
            Task run = model.SendAsync();
            var card = view.FindControl<AgentPermissionCard>("ActivePermissionCard")!;
            var counter = card.FindControl<TextBlock>("PermissionQueueCounter")!;
            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.PendingPermissions.Count == 2 && counter.IsEffectivelyVisible);
            Assert.That(counter.Text, Is.EqualTo("1 / 2"));
            Assert.That(composer.IsEffectivelyVisible, Is.False);
            Capture(view, window, "phase1-approval-queue");

            Click(Action(card, AgentStrings.NextApproval));
            Assert.That(counter.Text, Is.EqualTo("2 / 2"));
            Assert.That(model.ActivePermission!.ToolName, Is.EqualTo("set_element_timing"));
            Click(Action(card, AgentStrings.PreviousApproval));
            Assert.That(counter.Text, Is.EqualTo("1 / 2"));
            Click(card.FindControl<Button>("SubmitPermission")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.PendingPermissions.Count == 1 && !counter.IsEffectivelyVisible);
            Assert.That(model.ActivePermission!.ToolName, Is.EqualTo("set_element_timing"));
            Click(card.FindControl<Button>("SubmitPermission")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => composer.IsEffectivelyVisible);
            Assert.That(model.IsRunning, Is.True);
            release.SetResult();
            await run.WaitAsync(s_timeout);
            Assert.That(decisions, Is.EqualTo(new[] { AgentPermissionDecision.AllowOnce, AgentPermissionDecision.AllowOnce }));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task DraftsReloadAndHistoryKeysRespectMultilineInputAndSuggestions()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace
        {
            BundledSkills = [new("review-cut", "Review the cut", AgentComposerSuggestionKind.Skill)]
        };
        Guid first;
        Guid second;
        string attachment = Path.Combine(workspace.Root, "title-notes.txt");
        var provider = workspace.GetProviders().Single();
        var existing = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "Existing thread");
        await workspace.Runtime.RunTurnAsync(workspace.ProjectPath, existing.Id, provider, "First sent prompt", []);
        using (var tab = workspace.CreateWorkspace(workspace.Editor))
        {
            await tab.InitializeAsync();
            first = tab.SelectedThread!.Id;
            tab.Prompt = "Second sent prompt\nWith notes";
            await tab.SendAsync();
            tab.Prompt = "Unsent first draft";
            tab.IncludeSceneContext = false;
            await tab.PickFilesAsync(() => Task.FromResult<IReadOnlyList<string>>([attachment]));
            await tab.NewThreadAsync();
            tab.Prompt = "Other conversation";
            await tab.SendAsync();
            second = tab.SelectedThread!.Id;
            tab.Prompt = "Unsent second draft";
            await tab.NewThreadAsync();
            tab.Prompt = "Draft for a new thread";
            await tab.SelectThreadAsync(tab.Threads.Single(thread => thread.Id == first));
            Assert.That(tab.Prompt, Is.EqualTo("Unsent first draft"));
            Assert.That(tab.Attachments.Single().Path, Is.EqualTo(attachment));
            Assert.That(tab.IncludeSceneContext, Is.False);
        }

        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        await model.SelectThreadAsync(model.Threads.Single(thread => thread.Id == second));
        Assert.That(model.Prompt, Is.EqualTo("Unsent second draft"));
        await model.NewThreadAsync();
        Assert.That(model.Prompt, Is.EqualTo("Draft for a new thread"));
        await model.SelectThreadAsync(model.Threads.Single(thread => thread.Id == first));
        Assert.That(model.Prompt, Is.EqualTo("Unsent first draft"));
        Assert.That(model.IncludeSceneContext, Is.False);
        Assert.That(model.Attachments.Single().Path, Is.EqualTo(attachment));

        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            var popup = composer.FindControl<Popup>("SuggestionsPopup")!;
            model.Prompt = null;
            composer.FocusPrompt();
            Press(window, PhysicalKey.ArrowUp);
            Assert.That(prompt.Text?.ReplaceLineEndings("\n"), Is.EqualTo("Second sent prompt\nWith notes"));
            composer.FocusPrompt();
            Press(window, PhysicalKey.ArrowUp);
            Assert.That(prompt.Text?.ReplaceLineEndings("\n"), Is.EqualTo("Second sent prompt\nWith notes"));
            Assert.That(prompt.CaretIndex, Is.LessThan("Second sent prompt".Length));
            Press(window, PhysicalKey.ArrowUp);
            Assert.That(prompt.Text, Is.EqualTo("First sent prompt"));
            Press(window, PhysicalKey.ArrowDown);
            Assert.That(prompt.Text?.ReplaceLineEndings("\n"), Is.EqualTo("Second sent prompt\nWith notes"));
            Press(window, PhysicalKey.ArrowDown);
            Assert.That(prompt.Text, Is.Null.Or.Empty);
            Assert.That(model.Attachments.Single().Path, Is.EqualTo(attachment));

            window.KeyTextInput("Line one");
            Press(window, PhysicalKey.Enter, RawInputModifiers.Shift);
            window.KeyTextInput("Line two");
            Press(window, PhysicalKey.ArrowUp);
            Assert.That(prompt.Text?.ReplaceLineEndings("\n"), Is.EqualTo("Line one\nLine two"));
            model.Prompt = "/";
            composer.FocusPrompt();
            await HeadlessTestHelpers.WaitUntilAsync(() => popup.IsOpen);
            Press(window, PhysicalKey.ArrowUp);
            Assert.That(prompt.Text, Is.EqualTo("/"));
            Assert.That(popup.IsOpen, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ShutdownFlushesAnOpenNewThreadDraftAndRestoresItsSelection()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        model.Prompt = "Finished conversation";
        await model.SendAsync();
        await model.NewThreadAsync();
        model.Prompt = "Draft typed just before quitting";
        var state = new JsonObject();
        model.WriteToJson(state);
        using (var reopened = workspace.CreateWorkspace(workspace.Editor))
        {
            reopened.ReadFromJson(state);
            await reopened.InitializeAsync();
            Assert.That(reopened.SelectedThread, Is.Null);
        }

        await workspace.ThreadSessions.DisposeAsync();
        var reloaded = new AgentDraftStore(workspace.Root);
        Assert.That((await reloaded.LoadAsync(workspace.ProjectPath, null)).Draft!.Prompt,
            Is.EqualTo("Draft typed just before quitting"));
    }

    [AvaloniaTest]
    public async Task StopRetainsTheQueueAndUnsupportedSteerRunsThePromotedMessageNext()
    {
        await using var workspace = new AgentTabTestWorkspace();
        workspace.Bridge.SupportsSteer = false;
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        workspace.Bridge.Script = (request, ct) => request.Text.StartsWith("Wait", StringComparison.Ordinal)
            ? Task.Delay(Timeout.InfiniteTimeSpan, ct) : Task.CompletedTask;

        model.Prompt = "Wait until stopped";
        Task first = model.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => model.Timeline.OfType<AgentUserItem>().Any());
        model.Prompt = "Leave this in the queue";
        await model.SendAsync();
        await model.StopAsync();
        await first.WaitAsync(s_timeout);
        Assert.That(model.QueuedMessages.Single().Text, Is.EqualTo("Leave this in the queue"));

        model.Prompt = "Wait for the promoted input";
        Task second = model.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => model.Timeline.OfType<AgentUserItem>()
            .Any(item => item.Text == "Wait for the promoted input"));
        model.Prompt = "Promote this next";
        await model.SendAsync();
        await model.PromoteQueuedMessageAsync(model.QueuedMessages[1]);
        await second.WaitAsync(s_timeout);
        var users = model.Timeline.OfType<AgentUserItem>().ToArray();
        Assert.That(users.Select(item => item.Text), Is.EqualTo(new[]
        {
            "Wait until stopped", "Wait for the promoted input", "Promote this next", "Leave this in the queue"
        }));
        Assert.That(users[2].TurnId, Is.Not.EqualTo(users[1].TurnId));
        Assert.That(model.QueuedMessages, Is.Empty);
    }

    private static IEditorContext SceneEditor(string name)
    {
        var context = new Mock<IEditorContext>();
        context.SetupGet(editor => editor.Object).Returns(new Scene { Name = { Value = name } });
        return context.Object;
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Button Action(Control root, string name, AgentQueuedMessage? message = null)
        => root.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
            && AutomationProperties.GetName(button) == name && (message is null || ReferenceEquals(button.DataContext, message)));

    private static void Click(Control control) => HeadlessTestHelpers.Click(control, move: true, ticks: 4);

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        HeadlessTestHelpers.Render(4);
    }

    private static void Capture(AgentTabView view, Window window, string name)
    {
        window.MouseMove(new Point(4, 42));
        HeadlessTestHelpers.Render(6);
        HeadlessCapture.SaveIfRequested(view, string.Empty, name);
    }
}
