using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Agents;
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
public sealed class AgentTabPolishViewTests
{
    private const string Sol = "gpt-6.2-sol";
    private const string Astra = "gpt-6.2-astra";
    private const string Opus = "claude-opus-5-5";

    [AvaloniaTest]
    public async Task ComposerCompletesCommandsAndSkillsWithKeyboardAndPointer()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace
        {
            BundledSkills = [new("beutl-look", "Build an effect chain for the selected scene.", AgentComposerSuggestionKind.Skill, Source: "Beutl")]
        };
        workspace.Bridge.SuggestionCatalog = _ =>
        [
            new("compact", "Summarize the thread to free up context.", AgentComposerSuggestionKind.Command),
            new("compose", "Start composing a new sequence.", AgentComposerSuggestionKind.Command, "<scene>"),
            new("edit-guide", "Load the editing workflow.", AgentComposerSuggestionKind.Skill),
            new("beutl-look", "Duplicate entry", AgentComposerSuggestionKind.Skill)
        ];
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var view = new AgentTabView { DataContext = viewModel };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 580);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            var popup = composer.FindControl<Popup>("SuggestionsPopup")!;
            var list = composer.FindControl<ListBox>("SuggestionsList")!;
            composer.FocusPrompt();
            window.KeyTextInput("/");
            await HeadlessTestHelpers.WaitUntilAsync(() => popup.IsOpen && list.ItemCount == 5);
            HeadlessTestHelpers.Render(4);
            var composerCard = composer.FindControl<Border>("ComposerCard")!;
            Assert.That(list.TranslatePoint(default, window)!.Value.Y, Is.LessThan(composerCard.TranslatePoint(default, window)!.Value.Y));
            Assert.That(list.Items.Cast<AgentComposerSuggestionRow>().Single(item => item.Token == "/beutl-look").Suggestion!.Source, Is.EqualTo("Beutl"));
            HeadlessCapture.SaveIfRequested(view, string.Empty, "composer-slash-autocomplete");

            window.KeyTextInput("co");
            HeadlessTestHelpers.Render(3);
            Assert.That(list.Items.Cast<AgentComposerSuggestionRow>().Select(item => item.Token), Is.EqualTo(new[] { "/compact", "/compose" }));
            Press(window, PhysicalKey.ArrowDown);
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Press(window, PhysicalKey.ArrowUp);
            Assert.That(list.SelectedIndex, Is.Zero);
            Press(window, PhysicalKey.ArrowDown);
            Press(window, PhysicalKey.Enter);
            Assert.That(prompt.Text, Is.EqualTo("/compose "));
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(viewModel.IsRunning, Is.False);

            SetDraft("Prepare $", "Prepare $".Length);
            Assert.That(list.Items.Cast<AgentComposerSuggestionRow>().Select(item => item.Token), Is.EqualTo(new[] { "$beutl-look", "$edit-guide" }));
            HeadlessCapture.SaveIfRequested(view, string.Empty, "composer-skill-autocomplete");

            SetDraft("Prepare $edi-stale keep tail", "Prepare $edi".Length);
            Assert.That(list.Items.Cast<AgentComposerSuggestionRow>().Single().Token, Is.EqualTo("$edit-guide"));
            Press(window, PhysicalKey.Tab);
            Assert.That(prompt.Text, Is.EqualTo("Prepare $edit-guide keep tail"));
            Assert.That(prompt.CaretIndex, Is.EqualTo("Prepare $edit-guide ".Length));
            Assert.That(prompt.IsFocused, Is.True);

            SetDraft("/c", 2);
            Assert.That(popup.IsOpen, Is.True);
            Press(window, PhysicalKey.Escape);
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(prompt.Text, Is.EqualTo("/c"));
            SetDraft("/compa", 6);
            var option = list.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.IsEffectivelyVisible);
            Click(option);
            Assert.That(prompt.Text, Is.EqualTo("/compact "));
            Assert.That(prompt.IsFocused, Is.True);

            SetDraft("https://example.com/co", "https://example.com/co".Length);
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(viewModel.Timeline, Is.Empty);

            void SetDraft(string text, int caret)
            {
                prompt.Text = text;
                prompt.SelectionStart = caret;
                prompt.SelectionEnd = caret;
                prompt.CaretIndex = caret;
                HeadlessTestHelpers.Render(4);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task BackgroundWorkBarSplitsAtDockWidthAndFlyoutReusesNestedDetails()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = CreateWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var startAgents = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "shell", "Bash",
                IsBackground: true, BackgroundTitle: "ffmpeg -i trailer.mp4 -vf fps=1 preview-%03d.png"));
            workspace.Bridge.Emit(new AgentToolCallUpdated(thread, turn, now, "shell", AgentToolStatus.Running, "Rendering thumbnails…"));
            await startAgents.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "review", "Agent",
                Subagent: new("scene-reviewer", "Review the opening sequence")));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "tool", "render_still", "beutl", ParentToolCallId: "review"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "tool", AgentToolStatus.Completed,
                [new("text", "Captured the opening frame at 0.5 s")], ParentToolCallId: "review"));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "The title stays legible over the establishing shot.",
                "response", "review"));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "audio", "Agent",
                Subagent: new("audio-reviewer", "Check the audio levels")));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "audio", AgentToolStatus.Completed));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "I’m reviewing the scene and generating thumbnails.", "response"));
            await finish.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "shell", AgentToolStatus.Completed, [new("text", "Rendered 12 thumbnails.")]));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "review", AgentToolStatus.Completed));
        };

        var view = new AgentTabView { DataContext = viewModel };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 570);
        window.Show();
        viewModel.Prompt = "Review the opening sequence and prepare thumbnails.";
        var sending = viewModel.SendAsync();
        try
        {
            var bar = view.GetVisualDescendants().OfType<AgentBackgroundWorkBar>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() => bar.IsEffectivelyVisible);
            HeadlessTestHelpers.Render(4);
            var commands = bar.FindControl<ContentControl>("BackgroundCommandSection")!;
            var agents = bar.FindControl<Panel>("SubagentSection")!;
            double singleWidth = commands.Bounds.Width;
            Assert.That(agents.IsEffectivelyVisible, Is.False);

            startAgents.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.Subagents.Items.Count == 2 && viewModel.Subagents.Counter == "1/2");
            HeadlessTestHelpers.Render(4);
            var composerCard = Composer(view).FindControl<Border>("ComposerCard")!;
            Assert.That(agents.IsEffectivelyVisible, Is.True);
            Assert.That(commands.Bounds.Width, Is.EqualTo(agents.Bounds.Width).Within(1));
            Assert.That(commands.Bounds.Width, Is.LessThan(singleWidth));
            Assert.That(bar.Bounds.Width, Is.EqualTo(composerCard.Bounds.Width).Within(1));
            Assert.That(bar.TranslatePoint(default, window)!.Value.Y, Is.LessThan(composerCard.TranslatePoint(default, window)!.Value.Y));
            HeadlessCapture.SaveIfRequested(view, string.Empty, "background-work-split-bar");

            var timeline = view.GetVisualDescendants().OfType<AgentTimelineView>().Single();
            var worker = timeline.GetVisualDescendants().OfType<AgentToolView>()
                .Single(tool => tool.DataContext is AgentToolItem { Title: "Review the opening sequence" });
            OpenWorkDetails(worker);
            var nested = worker.GetVisualDescendants().OfType<AgentToolView>().Single(tool => tool.IsEffectivelyVisible);
            Assert.That(nested.IsEffectivelyVisible, Is.True);
            Assert.That(nested.DataContext, Is.TypeOf<AgentToolItem>());
            Assert.That(worker.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => text.IsEffectivelyVisible
                && text.Text == "The title stays legible over the establishing shot."), Is.True);
            Assert.That(viewModel.Timeline.OfType<AgentAssistantItem>().Single().Text, Is.EqualTo("I’m reviewing the scene and generating thumbnails."));
            HeadlessCapture.SaveIfRequested(view, string.Empty, "subagent-timeline");

            var card = bar.FindControl<Border>("WorkCard")!;
            var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(card)!;
            Click(commands.GetVisualDescendants().OfType<Button>().Single());
            Assert.That(flyout.IsOpen, Is.True);
            var flyoutContent = (Control)flyout.Content!;
            Assert.That(flyoutContent.TranslatePoint(default, window)!.Value.Y, Is.LessThan(card.TranslatePoint(default, window)!.Value.Y));
            var flyoutTools = flyoutContent.GetVisualDescendants().OfType<AgentToolView>().ToArray();
            var command = flyoutTools.Single(tool => tool.DataContext is AgentToolItem { IsBackground: true });
            var subagent = flyoutTools.Single(tool => tool.DataContext is AgentToolItem { Title: "Review the opening sequence" });
            OpenWorkDetails(command);
            OpenWorkDetails(subagent);
            Assert.That(subagent.GetVisualDescendants().OfType<AgentToolView>().Any(tool => tool.IsEffectivelyVisible), Is.True);
            Assert.That(command.GetVisualDescendants().OfType<SelectableTextBlock>().Any(text => text.IsEffectivelyVisible
                && text.Text == "Rendering thumbnails…"), Is.True);
            HeadlessCapture.SaveIfRequested(view, string.Empty, "background-work-flyout");
            flyout.Hide();
        }
        finally
        {
            startAgents.TrySetResult();
            finish.TrySetResult();
            await sending.WaitAsync(TimeSpan.FromSeconds(10));
            window.Close();
            HeadlessTestHelpers.Settle();
        }
        Assert.That(viewModel.Subagents.Counter, Is.EqualTo("2/2"));
        Assert.That(viewModel.BackgroundCommands.IsRunning, Is.False);

        static void OpenWorkDetails(AgentToolView tool)
        {
            var expander = tool.FindControl<Expander>("WorkExpander")!;
            Assert.That(expander.Theme, Is.SameAs(expander.FindResource("ToolTabExpanderTheme")));
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ExpanderHeader");
            Click(header);
            Assert.That(expander.IsExpanded, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task AddMenuPicksFilesCapturesAFrameAndTogglesTheSceneCard()
    {
        using var culture = new JapaneseCulture();
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = CreateWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        string filePath = Path.Combine(workspace.Root, "opening-brief.txt");
        await File.WriteAllTextAsync(filePath, "Opening title");
        AgentTabViewTests.CreatePreview(Path.Combine(workspace.Root, "frame.png"));

        var file = new Mock<IStorageFile>();
        file.SetupGet(value => value.Path).Returns(new Uri(filePath));
        var storage = new Mock<IStorageProvider>();
        storage.Setup(value => value.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>())).ReturnsAsync(new[] { file.Object });
        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateWindow(view, ThemeVariant.Dark);
        TestStorageProviderFactory.SetProvider(window, storage.Object);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var add = composer.FindControl<Button>("AddButton")!;
            var flyout = (MenuFlyout)add.Flyout!;
            var sceneContext = MenuAction(flyout, AgentStrings.AttachSceneContext);
            Click(add);
            Assert.That(flyout.IsOpen, Is.True);
            Assert.That(sceneContext.TranslatePoint(default, window)!.Value.Y, Is.LessThan(add.TranslatePoint(default, window)!.Value.Y));
            Assert.That(sceneContext.IsChecked, Is.True);

            Click(sceneContext);
            Assert.That(flyout.IsOpen, Is.False);
            Assert.That(viewModel.IncludeSceneContext, Is.False);
            Assert.That(composer.FindControl<Button>("SceneContextChip")!.IsEffectivelyVisible, Is.False);
            Assert.That(viewModel.Attachments, Is.Empty);
            Click(add);
            Click(MenuAction(flyout, AgentStrings.AttachFile));
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.Attachments.Count == 1 && viewModel.CanManage);
            storage.Verify(value => value.OpenFilePickerAsync(It.Is<FilePickerOpenOptions>(options => options.AllowMultiple)), Times.Once);
            Assert.That(viewModel.Attachments[0].Label, Is.EqualTo("opening-brief.txt"));

            Click(add);
            Click(MenuAction(flyout, AgentStrings.AttachCurrentFrame));
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.Attachments.Count == 2 && viewModel.CanManage);
            var frame = viewModel.Attachments.Single(attachment => attachment.Label == AgentStrings.CurrentFrame);
            var thumbnail = frame.Thumbnail!;
            Assert.That(thumbnail.PixelSize.Width, Is.EqualTo(216));
            Click(add);
            Click(sceneContext);
            var sceneChip = composer.FindControl<Button>("SceneContextChip")!;
            Assert.That(sceneChip.IsEffectivelyVisible, Is.True);
            Assert.That(ToolTip.GetTip(sceneChip), Is.EqualTo(viewModel.SceneContextTooltip));
            Assert.That(viewModel.SceneContextTooltip, Does.Contain("オープニング"));
            Assert.That(composer.GetVisualDescendants().OfType<Image>().Single(image => image.Source == thumbnail).IsEffectivelyVisible, Is.True);
            Click(add);
            Assert.That(flyout.IsOpen, Is.True);
            Capture(view, window, "dark-docked-attachments");
            flyout.Hide();

            var removeFrame = composer.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.DataContext, frame) && AutomationProperties.GetName(button) == AgentStrings.RemoveAttachment);
            var removeBadge = (Border)removeFrame.Parent!;
            Assert.That(removeBadge.IsHitTestVisible, Is.False);
            Assert.That(removeBadge.Opacity, Is.EqualTo(0));
            window.MouseMove(HeadlessTestHelpers.Center(removeFrame, window));
            await HeadlessTestHelpers.WaitUntilAsync(() => removeBadge.IsHitTestVisible && removeBadge.Opacity > 0.9);
            Assert.That(removeBadge.IsHitTestVisible, Is.True);
            Assert.That(removeBadge.Opacity, Is.GreaterThan(0.9));
            HeadlessCapture.SaveIfRequested(view, string.Empty, "dark-docked-attachments-hover");

            Click(removeFrame);
            Assert.That(frame.Thumbnail, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => _ = thumbnail.PixelSize);

            window.MouseMove(HeadlessTestHelpers.Center(sceneChip, window));
            HeadlessTestHelpers.Render(4);
            Assert.That(sceneChip.GetVisualDescendants().OfType<FluentIcons.Avalonia.Fluent.FluentIcon>()
                .Single(icon => icon.Classes.Contains("agent-scene-icon-hover")).IsEffectivelyVisible, Is.True);
            HeadlessCapture.SaveIfRequested(view, string.Empty, "dark-docked-scene-hover");
            Click(sceneChip);
            Assert.That(viewModel.IncludeSceneContext, Is.False);
            Assert.That(sceneChip.IsEffectivelyVisible, Is.False);

            await viewModel.CaptureFrameAsync();
            var remaining = viewModel.Attachments.Single(attachment => attachment.Thumbnail is not null).Thumbnail!;
            viewModel.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _ = remaining.PixelSize);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ModelPickerSearchesProvidersSavesFavoritesAndSelectsEffort()
    {
        using var culture = new JapaneseCulture();
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = CreateWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateWindow(view, ThemeVariant.Dark);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var trigger = composer.FindControl<Button>("ModelButton")!;
            var popover = (Flyout)trigger.Flyout!;
            var picker = (AgentModelPickerView)popover.Content!;
            Click(trigger);
            Assert.That(popover.IsOpen, Is.True);
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.ModelRows.Any(row => row.Model.Id == "gpt-5.4-pro"));
            var search = picker.FindControl<TextBox>("ModelSearchBox")!;
            var modelList = picker.FindControl<ListBox>("ModelRows")!;
            var effortList = picker.FindControl<ListBox>("EffortRows")!;
            await HeadlessTestHelpers.WaitUntilAsync(() => search.IsFocused);
            Assert.That(search.InnerLeftContent, Is.InstanceOf<FluentIcons.Avalonia.Fluent.FluentIcon>());
            Assert.That(popover.FlyoutPresenterTheme, Is.Null);
            Assert.That(modelList.ItemCount, Is.EqualTo(4));
            Assert.That(effortList.ItemCount, Is.GreaterThan(0));
            Assert.That(picker.FindControl<ItemsControl>("ModelProviderRows")!.GetVisualDescendants().OfType<ToggleButton>().Count(), Is.EqualTo(3));
            search.Focus();
            Capture(view, window, "dark-docked-model-picker");
            Click(picker.FindControl<Button>("ShowMoreModels")!);
            Assert.That(viewModel.ModelRows.Any(row => row.Model.Id == "gpt-5.4-mini"), Is.True);

            Click(Action(picker, "Claude Code"));
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.ModelRows.Any(row => row.Model.Id == Opus));
            var claudeIcons = picker.GetVisualDescendants().OfType<AgentProviderIcon>()
                .Where(icon => icon.Kind == AgentProviderKind.ClaudeCode).ToArray();
            Assert.That(claudeIcons, Is.Not.Empty);
            Assert.That(claudeIcons.Select(icon => ((ISolidColorBrush)icon.Foreground!).Color), Is.All.EqualTo(Color.Parse("#D97757")));
            search.Text = "sonnet";
            HeadlessTestHelpers.Render(3);
            Assert.That(viewModel.ModelRows.Single().Model.DisplayName, Is.EqualTo("Claude Sonnet 5.5"));
            search.Text = "";
            HeadlessTestHelpers.Render(3);
            Click(Option(modelList, "Claude Opus 5.5"));
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SelectedProvider?.Descriptor.Id == "claude");
            Click(Option(effortList, AgentStrings.EffortHigh));
            var composerIcon = composer.GetVisualDescendants().OfType<AgentProviderIcon>().Single();
            Assert.That(((ISolidColorBrush)composerIcon.Foreground!).Color, Is.EqualTo(Color.Parse("#D97757")));
            Capture(view, window, "dark-docked-claude-picker");
            Assert.That(viewModel.ShortModelName, Is.EqualTo("Opus 5.5"));
            Assert.That(viewModel.ReasoningEffortLabel, Is.EqualTo(AgentStrings.EffortHigh));
            var name = composer.FindControl<TextBlock>("ShortModelLabel")!;
            var effort = composer.FindControl<TextBlock>("ReasoningEffortLabel")!;
            Assert.That(name.Text, Is.EqualTo("Opus 5.5"));
            Assert.That(effort.Text, Is.EqualTo(AgentStrings.EffortHigh));
            Assert.That(((ISolidColorBrush)name.Foreground!).Color, Is.Not.EqualTo(((ISolidColorBrush)effort.Foreground!).Color));

            Assert.That(popover.IsOpen, Is.True);
            Click(Action(picker, "Studio ACP"));
            Assert.That(popover.IsOpen, Is.True);
            Assert.That(viewModel.ModelProviders.Single(row => row.IsSelected).Provider.Descriptor.Id, Is.EqualTo("studio"));
            search.Text = "studio/custom-v2";
            HeadlessTestHelpers.Render(3);
            Assert.That(viewModel.ModelRows.Select(row => row.Model.Id), Is.EqualTo(new[] { "studio/custom-v2" }));
            var custom = modelList.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.IsEffectivelyVisible && item.DataContext is AgentModelRow { Model.Id: "studio/custom-v2" });
            Assert.That(AutomationProperties.GetName(custom), Does.Contain("studio/custom-v2"));
            Click(custom);
            Assert.That(viewModel.ShortModelName, Is.EqualTo("studio/custom-v2"));
            Assert.That(viewModel.ReasoningEffortLabel, Is.Null);

            search.Text = "";
            Click(Action(picker, "Codex"));
            Click(Option(modelList, "GPT-6.2 Sol"));
            Click(Option(effortList, AgentStrings.EffortMax));
            Assert.That(viewModel.ReasoningEffortLabel, Is.EqualTo(AgentStrings.EffortMax));
            Assert.That(viewModel.ModelEfforts.Single(row => row.IsDefault).Label, Is.EqualTo(AgentStrings.EffortLow));
            var footer = picker.GetVisualDescendants().OfType<AgentPermissionModePicker>().Single();
            var footerCombo = footer.FindControl<ComboBox>("PermissionComboBox")!;
            footerCombo.SelectedItem = viewModel.PermissionModes.Single(mode => mode.Mode == AgentPermissionMode.Supervised);
            HeadlessTestHelpers.Render(3);
            Assert.That(viewModel.SelectedPermissionMode.Mode, Is.EqualTo(AgentPermissionMode.Supervised));
            popover.Hide();
            viewModel.Prompt = "Review the opening title.";
            await viewModel.SendAsync();
            Assert.That(workspace.Bridge.OpenedSession?.Model, Is.EqualTo(Sol));
            Assert.That(workspace.Bridge.OpenedSession?.ReasoningEffort, Is.EqualTo("max"));
            window.Width = 500;
            HeadlessTestHelpers.Render(4);
            var composerPermission = composer.FindControl<AgentPermissionModePicker>("PermissionPicker")!;
            var composerPermissionCombo = composerPermission.FindControl<ComboBox>("PermissionComboBox")!;
            Assert.That(composerPermission.IsCompact, Is.False);
            var wideIcon = composerPermissionCombo.GetVisualDescendants().OfType<FluentIcons.Avalonia.Fluent.FluentIcon>().Single();
            Point wideOrigin = wideIcon.TranslatePoint(default, composerPermission)!.Value;
            double wideCenterY = wideOrigin.Y + (wideIcon.Bounds.Height / 2);
            Capture(view, window, "dark-docked-wide");

            window.Width = 300;
            HeadlessTestHelpers.Render(4);
            Assert.That(composerPermission.IsCompact, Is.True);
            Assert.That(composerPermissionCombo.GetVisualDescendants().OfType<Control>()
                .Single(control => control.Name == "DropDownGlyph").IsEffectivelyVisible, Is.False);
            Assert.That(composerPermissionCombo.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Classes.Contains("agent-permission-label")).IsEffectivelyVisible, Is.False);
            var compactIcon = composerPermissionCombo.GetVisualDescendants().OfType<FluentIcons.Avalonia.Fluent.FluentIcon>().Single();
            Point compactOrigin = compactIcon.TranslatePoint(default, composerPermission)!.Value;
            double compactCenterY = compactOrigin.Y + (compactIcon.Bounds.Height / 2);
            double compactCenterX = compactOrigin.X + (compactIcon.Bounds.Width / 2);
            Assert.That(Math.Abs(compactOrigin.X - wideOrigin.X), Is.LessThanOrEqualTo(0.1),
                $"Compact icon X ({compactOrigin.X:0.##}) drifted from wide icon X ({wideOrigin.X:0.##}).");
            Assert.That(Math.Abs(compactOrigin.Y - wideOrigin.Y), Is.LessThanOrEqualTo(0.1),
                $"Compact icon Y ({compactOrigin.Y:0.##}) drifted from wide icon Y ({wideOrigin.Y:0.##}).");
            Assert.That(Math.Abs(compactCenterY - wideCenterY), Is.LessThanOrEqualTo(0.1),
                $"Compact icon center Y ({compactCenterY:0.##}) drifted from wide icon center Y ({wideCenterY:0.##}).");
            Assert.That(Math.Abs(compactCenterX - (composerPermissionCombo.Bounds.Width / 2)), Is.LessThanOrEqualTo(0.1),
                $"Compact icon center X ({compactCenterX:0.##}) is not centered inside button width ({composerPermissionCombo.Bounds.Width:0.##}).");
            Assert.That(Math.Abs(compactCenterY - (composerPermissionCombo.Bounds.Height / 2)), Is.LessThanOrEqualTo(0.1),
                $"Compact icon center Y ({compactCenterY:0.##}) is not centered inside button height ({composerPermissionCombo.Bounds.Height:0.##}).");
            Capture(view, window, "dark-docked-narrow");
            Click(trigger);
            Assert.That(picker.Bounds.Width, Is.LessThanOrEqualTo(composer.Bounds.Width));
            Assert.That(Grid.GetRow(picker.FindControl<Border>("EffortPane")!), Is.EqualTo(1));
            Click(Option(modelList, "GPT-6.2 Astra"));
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SelectedThread?.Model == Astra && viewModel.CanManage);
            Assert.That(popover.IsOpen, Is.True);
            var high = Option(effortList, AgentStrings.EffortHigh);
            high.BringIntoView();
            HeadlessTestHelpers.Render(3);
            Click(high);
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SelectedThread?.ReasoningEffort == "high" && viewModel.CanManage);
            popover.Hide();
            viewModel.Prompt = "Review the closing title.";
            await viewModel.SendAsync();
            Assert.That(workspace.Bridge.OpenedSession?.Model, Is.EqualTo(Astra));
            Assert.That(workspace.Bridge.OpenedSession?.ReasoningEffort, Is.EqualTo("high"));

            using var restored = workspace.CreateWorkspace(workspace.Editor);
            await restored.InitializeAsync();
            await restored.OpenModelPickerAsync();
            Assert.That(restored.SelectedThread?.Model, Is.EqualTo(Astra));
            Assert.That(restored.SelectedThread?.ReasoningEffort, Is.EqualTo("high"));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public Task ApprovalSupportsArrowAndNumberKeysThenEnter() => ExerciseApprovalAsync(ThemeVariant.Dark);

    [AvaloniaTest]
    public Task LightThemeApprovalAndComposerSupportMouseSubmission() => ExerciseApprovalAsync(ThemeVariant.Light);

    private static async Task ExerciseApprovalAsync(ThemeVariant variant)
    {
        using var culture = new JapaneseCulture();
        using var theme = AgentTabViewTests.UseDockTheme(variant);
        await using var workspace = CreateWorkspace(AgentPermissionMode.Supervised);
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var decisions = new List<AgentPermissionDecision>();
        bool dark = variant == ThemeVariant.Dark;
        string? frame = null;
        if (!dark)
        {
            frame = AgentTabViewTests.CreatePreview(Path.Combine(workspace.Root, "frame.png"));
            await viewModel.CaptureFrameAsync();
        }
        else
        {
            viewModel.IncludeSceneContext = false;
        }
        workspace.Bridge.Script = async (request, ct) =>
        {
            if (frame is not null)
            {
                var now = DateTimeOffset.UtcNow;
                workspace.Bridge.Emit(new AgentToolCallStarted(request.ThreadId, request.TurnId, now, "preview", "render_still", "beutl"));
                workspace.Bridge.Emit(new AgentToolCallCompleted(request.ThreadId, request.TurnId, now, "preview", AgentToolStatus.Completed,
                    [new AgentToolOutput("image", Data: frame)]));
            }
            decisions.Add(await workspace.Bridge.AskPermissionAsync(request, "dotnet test", null, ct, "dotnet test --nologo"));
            decisions.Add(await workspace.Bridge.AskPermissionAsync(request, "read scene notes", null, ct, options:
                [new(AgentPermissionDecision.AllowOnce, "Allow"), new(AgentPermissionDecision.RejectOnce, "Reject")]));
        };
        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateWindow(view, variant);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            viewModel.Prompt = "タイムラインの編集を確認してください。";
            var composer = Composer(view);
            Click(composer.FindControl<Button>("SendButton")!);
            var card = await WaitForApprovalAsync(view);
            var first = (AgentPermissionItem)card.DataContext!;
            var options = card.FindControl<ListBox>("PermissionOptions")!;
            Assert.That(composer.IsEffectivelyVisible, Is.False);
            Assert.That(Grid.GetRow(card), Is.EqualTo(Grid.GetRow(composer)));
            Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>(), Is.Empty);
            Assert.That(view.GetVisualDescendants().OfType<AgentPermissionCard>().Single(), Is.SameAs(card));
            Assert.That(options.SelectedIndex, Is.Zero);
            Assert.That(options.ItemCount, Is.EqualTo(3));
            Assert.That(options.ItemContainerTheme, Is.Null);
            Assert.That(card.FindControl<Button>("SubmitPermission")!.Classes.Contains("accent"), Is.True);
            await HeadlessTestHelpers.WaitUntilAsync(() => options.IsKeyboardFocusWithin);
            Capture(view, window, dark ? "dark-docked-approval" : "light-docked-approval");

            if (dark)
            {
                Press(window, PhysicalKey.ArrowDown);
                Assert.That(options.SelectedIndex, Is.EqualTo(1));
                Press(window, PhysicalKey.ArrowUp);
                Assert.That(options.SelectedIndex, Is.Zero);
                Press(window, PhysicalKey.Digit3);
                Assert.That(options.SelectedIndex, Is.EqualTo(2));
                Press(window, PhysicalKey.Digit1);
                Assert.That(options.SelectedIndex, Is.Zero);
                Press(window, PhysicalKey.Digit2);
                Assert.That(options.SelectedIndex, Is.EqualTo(1));
                Press(window, PhysicalKey.Enter);
            }
            else
            {
                Click(options.ContainerFromIndex(2)!);
                Assert.That(options.SelectedIndex, Is.EqualTo(2));
                Assert.That(viewModel.ActivePermission, Is.SameAs(first));
                Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>(), Is.Empty);
                Click(card.FindControl<Button>("SubmitPermission")!);
            }
            Assert.That(await WaitForApprovalAsync(view), Is.SameAs(card));
            options = card.FindControl<ListBox>("PermissionOptions")!;
            Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>().Single(), Is.SameAs(first));
            Assert.That(viewModel.ActivePermission, Is.Not.SameAs(first));
            Assert.That(options.ItemCount, Is.EqualTo(2));
            await HeadlessTestHelpers.WaitUntilAsync(() => options.IsKeyboardFocusWithin);
            var once = options.ContainerFromIndex(0)!;
            PointerPressedEventArgs? press = null;
            void RecordPress(object? sender, PointerPressedEventArgs e) => press = e;
            once.AddHandler(InputElement.PointerPressedEvent, RecordPress, RoutingStrategies.Tunnel);
            Click(once);
            once.RemoveHandler(InputElement.PointerPressedEvent, RecordPress);
            Assert.That(press, Is.Not.Null);
            // Headless mouse helpers render between events, which can exceed the double-click interval.
            once.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, press!));
            await HeadlessTestHelpers.WaitUntilAsync(() => !viewModel.IsRunning);
            Assert.That(composer.IsEffectivelyVisible, Is.True);
            Assert.That(card.IsEffectivelyVisible, Is.False);
            Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>().Select(item => item.IsPending), Is.All.False);
            Assert.That(decisions, Is.EqualTo(new[]
            {
                dark ? AgentPermissionDecision.AllowAlways : AgentPermissionDecision.RejectOnce, AgentPermissionDecision.AllowOnce
            }));
            var workToggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == "TurnStatsToggle");
            Assert.That(workToggle.IsChecked, Is.False);
            Click(workToggle);
            HeadlessTestHelpers.Render(4);
            Assert.That(view.GetVisualDescendants().OfType<Grid>().Count(grid => grid.Classes.Contains("agent-resolution") && grid.IsEffectivelyVisible),
                Is.EqualTo(2));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static AgentTabTestWorkspace CreateWorkspace(AgentPermissionMode defaultPermissionMode = AgentPermissionMode.Auto)
    {
        var scene = new Scene { Name = { Value = "オープニング" } };
        string executable = Environment.ProcessPath!;
        var workspace = new AgentTabTestWorkspace
        {
            Editor = Mock.Of<IEditorContext>(editor => editor.Object == scene),
            DefaultProviderId = "codex",
            DefaultPermissionMode = defaultPermissionMode,
            DefaultModel = Sol,
            DefaultReasoningEffort = "max",
            ProviderDescriptors =
            [
                AgentProviderDescriptor.Codex with { Command = executable },
                AgentProviderDescriptor.ClaudeCode with { Command = executable },
                new("studio", AgentProviderKind.Acp, "Studio ACP", executable)
            ]
        };
        workspace.Bridge.ModelCatalog = request => request.ProviderId switch
        {
            "codex" =>
            [
                new(Sol, "GPT-6.2 Sol", "6.2 Sol", "codex", ["low", "medium", "high", "xhigh", "max"], "low", IsProviderDefault: true),
                new(Astra, "GPT-6.2 Astra", "6.2 Astra", "codex", ["low", "medium", "high", "xhigh", "max"], "low"),
                new("gpt-5.4-pro", "GPT-5.4 Pro", "5.4 Pro", "codex", ["low", "medium", "high"], "medium"),
                new("gpt-5.3-codex", "GPT-5.3 Codex", "5.3 Codex", "codex", ["low", "medium", "high"], "medium"),
                new("gpt-5.4-mini", "GPT-5.4 Mini", "5.4 Mini", "codex", ["low", "medium", "high"], "medium", IsSecondary: true)
            ],
            "claude" =>
            [
                new(Opus, "Claude Opus 5.5", "Opus 5.5", "claude", ["low", "medium", "high", "xhigh"], "medium"),
                new("claude-sonnet-5-5", "Claude Sonnet 5.5", "Sonnet 5.5", "claude", ["low", "medium", "high"], "medium")
            ],
            _ => []
        };
        return workspace;
    }

    private static async Task<AgentPermissionCard> WaitForApprovalAsync(AgentTabView view)
    {
        var card = view.FindControl<AgentPermissionCard>("ActivePermissionCard")!;
        await HeadlessTestHelpers.WaitUntilAsync(() => card.IsEffectivelyVisible
            && card.DataContext is AgentPermissionItem { IsPending: true });
        HeadlessTestHelpers.Render(4);
        return card;
    }

    private static Window CreateWindow(AgentTabView view, ThemeVariant variant) =>
        AgentTabViewTests.CreateDockWindow(view, variant, 550, 586);

    private static AgentComposerView Composer(AgentTabView view) => view.GetVisualDescendants().OfType<AgentComposerView>().Single();

    private static Button Action(Control root, string name) => root.GetVisualDescendants().OfType<Button>()
        .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name);

    private static ListBoxItem Option(ListBox list, string name) => list.GetVisualDescendants().OfType<ListBoxItem>()
        .Single(item => item.IsEffectivelyVisible && AutomationProperties.GetName(item) == name);

    private static MenuItem MenuAction(MenuFlyout flyout, string label) => flyout.Items.OfType<MenuItem>()
        .Single(item => Equals(item.Header, label));

    private static void Click(Control target) => HeadlessTestHelpers.Click(target, move: true, ticks: 3);

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        HeadlessTestHelpers.Render(3);
    }

    private static void Capture(AgentTabView view, Window window, string name)
    {
        window.MouseMove(new Point(4, 42));
        HeadlessTestHelpers.Render(4);
        HeadlessCapture.SaveIfRequested(view, string.Empty, name);
    }

    private sealed class JapaneseCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public JapaneseCulture() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
