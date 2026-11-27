using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Language;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentTabPhase4Tests
{
    [AvaloniaTest]
    public async Task MeterShowsLiveUsageAndCommandsResetOrCompactContext()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        workspace.Bridge.SuggestionCatalog = _ =>
            [new("compact", "Provider variant", AgentComposerSuggestionKind.Command), new("clear", "Provider variant", AgentComposerSuggestionKind.Command)];
        workspace.Bridge.Script = (request, _) =>
        {
            var (thread, turn, text, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentSessionStateChanged(thread, turn, now, AgentSessionState.Open, "meter-session"));
            var usage = text.Contains("次の", StringComparison.Ordinal) ? new AgentContextUsage(96_000, 100_000, 90_000, 6_000, 40_000)
                : new AgentContextUsage(84_000, 100_000, 80_000, 4_000, 32_000);
            workspace.Bridge.Emit(new AgentContextUsageUpdated(thread, turn, now, usage));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "冒頭のカットを確認しました。"));
            return Task.CompletedTask;
        };
        var compacting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCompaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Compact = async (thread, turn, ct) =>
        {
            compacting.TrySetResult();
            await finishCompaction.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentContextUsageUpdated(thread, turn, DateTimeOffset.UtcNow, new(12_000, 100_000, 10_000, 2_000, 5_000)));
            return new(AgentCompactionStatus.Completed);
        };

        await model.InitializeAsync();
        var view = new AgentTabView { DataContext = model };
        var window = Open(view);
        try
        {
            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            double permissionWidth = Find<AgentPermissionModePicker>(composer, "PermissionPicker").Bounds.Width;
            model.Prompt = "冒頭のカットを確認して";
            await model.SendAsync();
            var bar = view.GetVisualDescendants().OfType<AgentThreadBar>().Single();
            var meter = Find<Border>(bar, "ContextMeter");
            var ring = meter.GetVisualDescendants().OfType<AgentContextRing>().Single();
            Assert.That(meter.IsEffectivelyVisible, Is.True);
            Assert.That(ring.Progress, Is.EqualTo(.84));
            Assert.That(ring.Classes, Does.Contain("warning"));
            model.DiscoverComposerSuggestions('/');
            var suggestions = model.GetComposerSuggestions(new AgentComposerToken(0, 1, '/', ""));
            Assert.That(suggestions.Select(row => row.Token).Where(token => token is "/clear" or "/compact"),
                Is.EqualTo(new[] { "/clear", "/compact" }));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanSend || !model.IsRunning);

            model.Prompt = "/compact";
            Click(Find<Button>(view, "SendButton"));
            await compacting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<AgentShimmerTextBlock>(view, "LiveTurnStatus")?.Text == AgentStrings.CompactingContext);
            finishCompaction.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManageContext && ring.Progress == .12);
            Assert.That(model.Timeline.OfType<AgentTimelineDividerItem>().Single().Text, Is.EqualTo(AgentStrings.ContextCompacted));

            ClickMenu(bar, AgentStrings.ClearContext);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManageContext && ring.Progress == 0
                && model.Timeline.OfType<AgentTimelineDividerItem>().Last().Text == AgentStrings.ContextCleared);
            var stored = new AgentThreadStore(workspace.Root);
            Assert.That((await stored.GetAsync(workspace.ProjectPath, model.SelectedThread!.Id))!.ExternalSessionId, Is.Null);

            model.Prompt = "次のカットを確認して";
            await model.SendAsync();
            Assert.That(ring.Classes, Does.Contain("critical"));
            window.MouseMove(meter.TranslatePoint(new Point(meter.Bounds.Width / 2, meter.Bounds.Height / 2), window)!.Value);
            ToolTip.SetIsOpen(meter, true);
            var tooltip = (StackPanel)ToolTip.GetTip(meter)!;
            await HeadlessTestHelpers.WaitUntilAsync(() => tooltip.Children.OfType<TextBlock>().All(text => !string.IsNullOrEmpty(text.Text)));
            var text = tooltip.Children.OfType<TextBlock>().Select(label => label.Text).ToArray();
            Assert.That(text[0], Does.Contain("96,000").And.Contain("100,000").And.Contain("96"));
            Assert.That(text.Skip(1), Is.EqualTo(new[] { model.ContextMeterInput, model.ContextMeterOutput, model.ContextMeterCache }));
            Assert.That(Find<Button>(composer, "ModelButton").Bounds.Width, Is.GreaterThan(40));
            Assert.That(Find<Button>(composer, "SceneContextChip").Bounds.Width, Is.GreaterThan(40));
            Assert.That(Find<AgentPermissionModePicker>(composer, "PermissionPicker").Bounds.Width, Is.EqualTo(permissionWidth));
            Capture(view, "phase4-context-meter");
            ToolTip.SetIsOpen(meter, false);

            model.Prompt = "/clear";
            Click(Find<Button>(view, "SendButton"));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManageContext && ring.Progress == 0);
            var timeline = new AgentThreadTimeline();
            timeline.Restore(await stored.ReadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id));
            Assert.That(timeline.ContextUsage, Is.EqualTo(new AgentContextUsage(0, 100_000, 0, 0, 0)));
            Assert.That(timeline.Items.OfType<AgentTimelineDividerItem>().Count(item => item.Text == AgentStrings.ContextCleared), Is.EqualTo(2));
            timeline.Clear();
            ClickMenu(bar, AgentStrings.CompactContext);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManageContext
                && model.Timeline.OfType<AgentTimelineDividerItem>().Last().Text == AgentStrings.ContextCompacted);
        }
        finally
        {
            finishCompaction.TrySetResult();
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task ModelPickerSwitchesProviderInBothTabsAndActionsRegenerateAndFork()
    {
        using var ui = new JapaneseDock();
        var codex = AgentProviderDescriptor.Codex with { Command = Environment.ProcessPath! };
        var claude = AgentProviderDescriptor.ClaudeCode with { Command = Environment.ProcessPath!, DisplayName = "Claude" };
        await using var workspace = new AgentTabTestWorkspace { ProviderDescriptors = [codex, claude], DefaultProviderId = codex.Id };
        using var first = workspace.CreateWorkspace(workspace.Editor);
        using var other = workspace.CreateWorkspace(workspace.Editor);
        AgentTurnRequest? last = null;
        workspace.Bridge.Script = (request, _) =>
        {
            last = request;
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow,
                "冒頭のタイトルをセーフエリアに収めました。"));
            return Task.CompletedTask;
        };
        await first.InitializeAsync();
        first.Prompt = "# **冒頭のタイトルを調整する**";
        await first.SendAsync();
        Assert.That(first.ThreadTitle, Is.EqualTo("冒頭のタイトルを調整する"));
        var source = first.SelectedThread!;
        await first.RenameThreadAsync(source, "仮のタイトル");
        await other.InitializeAsync();
        var view = new AgentTabView { DataContext = first };
        var window = Open(view);
        try
        {
            var modelButton = Find<Button>(view, "ModelButton");
            Click(modelButton);
            var flyout = (Flyout)modelButton.Flyout!;
            var picker = (AgentModelPickerView)flyout.Content!;
            var option = picker.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.DataContext is AgentModelProviderRow row && row.Provider.Descriptor.Id == claude.Id);
            Click(option);
            var rows = Find<ListBox>(picker, "ModelRows");
            rows.SelectedItem = first.ModelRows.Single(row => row.Model.Id == "sonnet");
            await HeadlessTestHelpers.WaitUntilAsync(() => first.CanManage && other.ProviderKind == AgentProviderKind.ClaudeCode);
            flyout.Hide();
            Assert.That(first.SelectedThread!.Model, Is.EqualTo("sonnet"));
            Assert.That(other.SelectedThread!.Model, Is.EqualTo("sonnet"));
            var expected = string.Format(AgentStrings.ProviderChangeNotice, codex.DisplayName, claude.DisplayName);
            Assert.That(first.Timeline.OfType<AgentTimelineDividerItem>().Single().Text, Is.EqualTo(expected));
            Assert.That(other.Timeline.OfType<AgentTimelineDividerItem>().Single().Text, Is.EqualTo(expected));
            Assert.That(modelButton.GetVisualDescendants().OfType<AgentProviderIcon>().Single().Kind, Is.EqualTo(AgentProviderKind.ClaudeCode));
            Capture(view, "phase4-provider-switch");

            first.Prompt = "そのまま調整を続けて";
            await first.SendAsync();
            Assert.That(last!.PreviousMessages!.Select(message => message.Role), Is.EqualTo(new[] { "user", "assistant" }));
            var bar = view.GetVisualDescendants().OfType<AgentThreadBar>().Single();
            ClickMenu(bar, AgentStrings.RegenerateThreadTitle);
            await HeadlessTestHelpers.WaitUntilAsync(() => first.CanManage && first.ThreadTitle == "冒頭のタイトルを調整する");
            Assert.That(other.ThreadTitle, Is.EqualTo(first.ThreadTitle));
            ClickMenu(bar, AgentStrings.ForkThread);
            await HeadlessTestHelpers.WaitUntilAsync(() => first.CanManage && first.SelectedThread?.Id != source.Id);
            Assert.That(first.SelectedThread!.ProviderId, Is.EqualTo(claude.Id));
            Assert.That(first.Timeline.OfType<AgentUserItem>().Count(), Is.EqualTo(2));
            Assert.That(first.Timeline.OfType<AgentTimelineDividerItem>().Single().Text, Is.EqualTo(expected));
            Assert.That(first.DisplayTimeline.OfType<AgentTurnWorkItem>(), Is.Empty);
            Assert.That(other.SelectedThread!.Id, Is.EqualTo(source.Id));
        }
        finally
        {
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task SwitcherPinsArchivesSearchesBodiesAndMarksBackgroundCompletionRead()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace();
        var store = new AgentThreadStore(workspace.Root);
        var provider = workspace.ProviderDescriptors.Single();
        var pinned = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "冒頭のメモ");
        var background = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "カラー調整");
        var archived = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "冒頭の旧案");
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        await model.SelectThreadAsync(pinned);
        var view = new AgentTabView { DataContext = model };
        var window = Open(view);
        var started = new TaskCompletionSource<AgentTurnRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (request, ct) =>
        {
            started.TrySetResult(request);
            await end.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(request.ThreadId, request.TurnId,
                DateTimeOffset.UtcNow, "冒頭のカットの露出を少し上げました。"));
        };
        try
        {
            var bar = view.GetVisualDescendants().OfType<AgentThreadBar>().Single();
            ClickMenu(bar, AgentStrings.PinThread);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.SelectedThread!.IsPinned);
            await model.SelectThreadAsync(archived);
            ClickMenu(bar, AgentStrings.ArchiveThread);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.SelectedThread!.IsArchived);
            Assert.That(model.RecentThreads.Select(row => row.Thread.Id), Does.Not.Contain(archived.Id));
            await model.SelectThreadAsync(background);
            model.Prompt = "カラーを調整して";
            var running = model.SendAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await model.SelectThreadAsync(pinned);
            end.SetResult();
            await running;
            Assert.That(model.FilteredThreads[0].Thread.Id, Is.EqualTo(pinned.Id));
            Assert.That(model.FilteredThreads.Single(row => row.Thread.Id == background.Id).IsUnread, Is.True);
            Assert.That(model.FilteredThreads.Select(row => row.Thread.Id), Does.Not.Contain(archived.Id));

            var switcher = Find<Button>(bar, "ThreadSwitcherButton");
            var switcherPresenter = switcher.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(presenter => presenter.Name == "PART_ContentPresenter");
            window.MouseMove(switcher.TranslatePoint(new Point(switcher.Bounds.Width / 2, switcher.Bounds.Height / 2), window)!.Value);
            await HeadlessTestHelpers.WaitUntilAsync(() => switcher.IsPointerOver);
            Assert.That(switcher.Opacity, Is.EqualTo(0.8));
            Assert.That(switcherPresenter.Background, Is.EqualTo(Brushes.Transparent));
            Click(switcher);
            await HeadlessTestHelpers.WaitUntilAsync(() => switcher.Opacity == 0.5);
            Assert.That(switcherPresenter.Background, Is.EqualTo(Brushes.Transparent));
            var search = bar.FindControl<TextBox>("ThreadSearchBox")!;
            var archivedToggle = bar.FindControl<CheckBox>("ShowArchivedThreads")!;
            Click(archivedToggle);
            search.Text = "冒頭";
            await HeadlessTestHelpers.WaitUntilAsync(() => model.FilteredThreads.Count == 3
                && model.FilteredThreads.Any(row => row.MatchSnippet?.Contains("露出", StringComparison.Ordinal) == true)
                && Visible<Control>(window, "ThreadUnreadDot") is not null);
            Assert.That(model.FilteredThreads[0].Thread.Id, Is.EqualTo(pinned.Id));
            Assert.That(model.FilteredThreads.Any(row => row.Thread is { IsArchived: true }), Is.True);
            Capture(view, "phase4-thread-organization");

            var matches = bar.FindControl<ItemsControl>("ThreadMatches")!;
            var rowActions = matches.GetVisualDescendants().OfType<Button>().Single(button => button is
            { Flyout: MenuFlyout, DataContext: AgentThreadRow row } && row.Thread.Id == archived.Id);
            Click(rowActions);
            var rowMenu = (MenuFlyout)rowActions.Flyout!;
            Click(rowMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, AgentStrings.UnarchiveThread)));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && !model.Threads.Single(thread => thread.Id == archived.Id).IsArchived);
            if (!((Flyout)switcher.Flyout!).IsOpen)
                Click(switcher);
            var target = matches.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == background.Title);
            Click(target);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.SelectedThread?.Id == background.Id);
            var latest = (await store.GetAsync(workspace.ProjectPath, background.Id))!;
            Assert.That(latest.LastReadAt, Is.Not.Null);
            Assert.That(latest.IsUnread, Is.False);
        }
        finally
        {
            end.TrySetResult();
            Close(window);
        }
    }

    [AvaloniaTest]
    public async Task WorkSectionContainsCommentaryAndMixedToolsThenCollapsesAboveFinalReply()
    {
        using var ui = new JapaneseDock();
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string frame = AgentTabPhase3Tests.CreateFrame(Path.Combine(workspace.Root, "frame.png"));
        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "まず冒頭のシーンとタイトルの位置を確認します。"));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "cmd", "Codex command",
                Input: System.Text.Json.JsonSerializer.SerializeToElement("git status --short")));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "cmd", AgentToolStatus.Failed,
                [new AgentToolOutput("text", "作業フォルダーに Git の履歴がありません。")]));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "mcp", "inspect_element", "beutl"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "mcp", AgentToolStatus.Completed));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "image", "render_still", "beutl"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "image", AgentToolStatus.Completed, [new AgentToolOutput("image", Data: frame)]));
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now, "タイトルを安全領域の内側へ移動すると読みやすくなります。"));
            ready.SetResult();
            await finish.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, DateTimeOffset.UtcNow,
                "冒頭のタイトルを安全領域に収め、フェードを 12 フレームにそろえました。"));
            workspace.Bridge.Emit(new AgentTurnUsageUpdated(thread, turn, DateTimeOffset.UtcNow, 1100, 200, 730));
        };
        var view = new AgentTabView { DataContext = model };
        var window = Open(view, 850);
        try
        {
            model.Prompt = "冒頭のシーンを点検して、タイトルの位置とフェードを整えて";
            Task running = model.SendAsync();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<AgentShimmerTextBlock>(view, "LiveTurnStatus")?.ShimmerMaskBrush is not null
                && Visible<ToggleButton>(view, "ToolGroupToggle") is not null
                && Visible<Button>(view, "ToolImageThumbnail") is not null);
            var header = Find<TextBlock>(view, "TurnWorkTime");
            var section = model.DisplayTimeline.OfType<AgentTurnWorkItem>().Single();
            Assert.That(model.DisplayTimeline, Is.EqualTo(new AgentTimelineItemViewModel[] { model.Timeline.OfType<AgentUserItem>().Single(), section }));
            var live = Visible<AgentShimmerTextBlock>(view, "LiveTurnStatus")!;
            var gradient = live.ShimmerMaskBrush!;
            double previous = gradient.StartPoint.Point.X;
            await HeadlessTestHelpers.WaitUntilAsync(() => gradient.StartPoint.Point.X != previous);
            Assert.That(live.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Left));
            Assert.That(live.TranslatePoint(default, view)!.Value.Y, Is.GreaterThan(header.TranslatePoint(default, view)!.Value.Y));
            Assert.That(Visible<StackPanel>(view, "AssistantFooter"), Is.Null);

            var tools = section.Entries.OfType<AgentToolGroupItem>().Single();
            Assert.That(tools.Tools.Select(tool => tool.ToolCallId), Is.EqualTo(new[] { "cmd", "mcp" }));
            Assert.That(tools.HasFailure, Is.True);
            Assert.That(Visible<TextBlock>(view, "ToolGroupFailure"), Is.Not.Null);
            Assert.That(view.GetVisualDescendants().OfType<FAProgressRing>().Where(control => control.IsEffectivelyVisible), Is.Empty);
            Click(Visible<ToggleButton>(view, "ToolGroupToggle")!);
            var command = view.GetVisualDescendants().OfType<ToggleButton>().Single(control => control.Name == "ToolToggle"
                && control.IsEffectivelyVisible && control.DataContext is AgentToolItem { ToolCallId: "cmd" });
            Click(command);
            Assert.That(command.IsChecked, Is.True);
            AssertDisclosureTransparent(command);
            AssertDisclosureTransparent(Find<ToggleButton>(view, "TurnStatsToggle"));
            var commandName = command.GetVisualDescendants().OfType<AgentShimmerTextBlock>().Single();
            var highlighted = ((ISolidColorBrush)commandName.Foreground!).Color;
            window.MouseMove(new Point(4, 4));
            HeadlessTestHelpers.Render(4);
            Assert.That(((ISolidColorBrush)commandName.Foreground!).Color, Is.Not.EqualTo(highlighted));
            await HeadlessTestHelpers.WaitUntilAsync(() => header.Text != string.Format(AgentStrings.TurnWorkingFor, AgentTurnTime.Format(0)));
            Capture(view, "phase4-turn-layout");

            finish.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(10));
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<StackPanel>(view, "AssistantFooter")?.DataContext is AgentAssistantItem { IsFinalTurnResponse: true });
            var summary = (AgentAssistantItem)model.DisplayTimeline.Last();
            Assert.That(summary.IsFinalTurnResponse, Is.True);
            Assert.That(summary.CanCopy, Is.True);
            Assert.That(section.Entries.OfType<AgentAssistantItem>().Single().CanCopy, Is.False);
            Assert.That(Find<TextBlock>(view, "TurnWorkTime"), Is.SameAs(header));
            Assert.That(Visible<AgentShimmerTextBlock>(view, "LiveTurnStatus"), Is.Null);
            Assert.That(live.ShimmerMaskBrush, Is.Null);
            Assert.That(live.Foreground, Is.Not.InstanceOf<LinearGradientBrush>());
            Assert.That(section.IsExpanded, Is.False);
            Assert.That(Visible<Button>(view, "ToolImageThumbnail"), Is.Null);
            Assert.That(Visible<StackPanel>(view, "AssistantFooter")?.DataContext, Is.SameAs(summary));
            window.Height = 580;
            Capture(view, "phase4-work-collapsed");

            var workToggle = Find<ToggleButton>(view, "TurnStatsToggle");
            var workChevron = workToggle.GetVisualDescendants().OfType<FluentIcons.Avalonia.Fluent.FluentIcon>().Single();
            var idleWorkColor = ((ISolidColorBrush)header.Foreground!).Color;
            var idleChevronColor = ((ISolidColorBrush)workChevron.Foreground!).Color;
            window.MouseMove(HeadlessTestHelpers.Center(workToggle, window));
            HeadlessTestHelpers.Render(4);
            Assert.That(((ISolidColorBrush)header.Foreground!).Color, Is.Not.EqualTo(idleWorkColor));
            Assert.That(((ISolidColorBrush)workChevron.Foreground!).Color, Is.Not.EqualTo(idleChevronColor));
            window.MouseMove(new Point(4, 4));
            HeadlessTestHelpers.Render(4);
            Assert.That(((ISolidColorBrush)header.Foreground!).Color, Is.EqualTo(idleWorkColor));
            Assert.That(((ISolidColorBrush)workChevron.Foreground!).Color, Is.EqualTo(idleChevronColor));

            Click(workToggle);
            AssertDisclosureTransparent(workToggle);
            Assert.That(section.IsExpanded, Is.True);
            Assert.That(Visible<Button>(view, "ToolImageThumbnail"), Is.Not.Null);
            Find<ScrollViewer>(view, "TimelineScroll").Offset = default;
            Capture(view, "phase4-work-expanded");

            var restored = new AgentThreadTimeline();
            restored.Restore(await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id));
            Assert.That(restored.Items[0], Is.TypeOf<AgentUserItem>());
            Assert.That(restored.Items[1], Is.TypeOf<AgentTurnStatsItem>());
            Assert.That(restored.Items.OfType<AgentAssistantItem>().Select(item => item.IsFinalTurnResponse), Is.EqualTo(new[] { false, true }));
            restored.Clear();
        }
        finally
        {
            finish.TrySetResult();
            Close(window);
        }
    }

    private static Window Open(AgentTabView view, int height = 580)
    {
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, height);
        window.Show();
        HeadlessTestHelpers.Render(5);
        return window;
    }

    private static void Close(Window window)
    {
        window.Close();
        HeadlessTestHelpers.Settle();
    }

    private static T Find<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    [AvaloniaTest]
    public void ShimmerSweepsContinuouslyAcrossMultipleWordsAndScripts()
    {
        var shimmer = new AgentShimmerTextBlock
        {
            Text = "Read get_scene ファイルを読み込み中",
            FontSize = 16,
            MutedBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            HighlightBrush = new SolidColorBrush(Colors.White),
            IsLive = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var host = new Border
        {
            Width = 360,
            Height = 40,
            Background = Brushes.Black,
            Padding = new Thickness(8, 0),
            Child = shimmer
        };
        var window = new Window
        {
            Width = 360,
            Height = 40,
            Content = host,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);

            double MeasureMaxLuminance(double phase, int startX, int endX)
            {
                shimmer.TestPhaseOverride = phase;
                shimmer.UpdateMask(0);
                HeadlessTestHelpers.Render(2);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(360, 40));
                bitmap.Render(host);
                using var stream = new MemoryStream();
                bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                stream.Position = 0;
                using var skBitmap = SkiaSharp.SKBitmap.Decode(stream);
                double max = 0;
                for (int y = 0; y < skBitmap.Height; y++)
                {
                    for (int x = startX; x < Math.Min(endX, skBitmap.Width); x++)
                    {
                        var pixel = skBitmap.GetPixel(x, y);
                        double luminance = (pixel.Red + pixel.Green + pixel.Blue) / 3.0;
                        if (luminance > max)
                            max = luminance;
                    }
                }
                return max;
            }

            // At phase 0.2 the shimmer band sits over the left Latin word ("Read") and not the right CJK text.
            double leftEarly = MeasureMaxLuminance(0.2, 8, 70);
            double rightEarly = MeasureMaxLuminance(0.2, 220, 320);
            // At phase 0.8 the shimmer band sits over the right CJK text and not the left Latin word.
            double leftLate = MeasureMaxLuminance(0.8, 8, 70);
            double rightLate = MeasureMaxLuminance(0.8, 220, 320);

            Assert.Multiple(() =>
            {
                Assert.That(leftEarly, Is.GreaterThan(150));
                Assert.That(leftEarly - rightEarly, Is.GreaterThan(50));
                Assert.That(rightLate, Is.GreaterThan(150));
                Assert.That(rightLate - leftLate, Is.GreaterThan(50));
            });
        }
        finally
        {
            Close(window);
        }
    }

    private static T? Visible<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name && control.IsEffectivelyVisible);

    private static void Click(Control control) => HeadlessTestHelpers.Click(control, move: true, ticks: 5);

    private static void AssertDisclosureTransparent(ToggleButton toggle)
    {
        var presenter = Find<ContentPresenter>(toggle, "PART_ContentPresenter");
        Assert.That(((ISolidColorBrush)presenter.Background!).Color, Is.EqualTo(Colors.Transparent));
        Assert.That(presenter.BorderThickness, Is.EqualTo(default(Thickness)));
    }

    private static void ClickMenu(AgentThreadBar bar, string header)
    {
        var actions = Find<Button>(bar, "ThreadActionsButton");
        Click(actions);
        var flyout = (MenuFlyout)actions.Flyout!;
        Click(flyout.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header)));
    }

    private static void Capture(Control control, string name)
    {
        HeadlessTestHelpers.Render(8);
        HeadlessCapture.SaveIfRequested(control, string.Empty, name);
    }

    private sealed class JapaneseDock : IDisposable
    {
        private readonly IDisposable _theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly bool _popups = TestAppBuilder.HeadlessOptions.OverlayPopups;

        public JapaneseDock()
        {
            var ja = CultureInfo.GetCultureInfo("ja-JP");
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = ja;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = ja;
            TestAppBuilder.HeadlessOptions.OverlayPopups = true;
        }

        public void Dispose()
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = _popups;
            CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
            _theme.Dispose();
        }
    }
}
