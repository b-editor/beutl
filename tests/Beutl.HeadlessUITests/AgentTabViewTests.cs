using System.Collections.Concurrent;
using System.Reactive.Disposables;
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
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Language;
using Beutl.Testing.Headless;
using LiveMarkdown.Avalonia;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AgentTabViewTests
{
    private const int DockWidth = 390;
    private const int DockHeight = 760;
    private const string ResponseIntro = "The title is legible over the wide shot.";
    private const string Response = "Set `FadeIn` to **2 seconds**. See [Beutl](https://github.com/b-editor/beutl)."
        + "\n\n| Setting | Value |\n| --- | --- |\n| Fade in | 2 seconds |";

    [Test]
    public void AgentTabStylesUseThemeBrushes()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        string styles = File.ReadAllText(Path.Combine(directory!.FullName,
            "src", "Beutl.Editor.Components", "AgentTab", "Views", "AgentTabStyles.axaml"));
        Assert.That(styles, Does.Not.Match(@"#[0-9A-Fa-f]{6,8}\b"));
        Assert.That(styles, Does.Not.Contain("<ControlTheme"));
    }

    [AvaloniaTest]
    public async Task EmptyStateStartsDraftsAndTheThreadSwitcherSearchesAndRenames()
    {
        using var theme = UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        var provider = workspace.GetProviders().Single();
        var opening = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "Balance the opening title", AgentPermissionMode.Supervised);
        await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "Plan the product cutaway", AgentPermissionMode.Supervised);
        await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, provider, "Review the end card", AgentPermissionMode.Supervised);
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        await viewModel.NewThreadAsync();

        var log = new UiLog();
        ILogSink? previousLog = Logger.Sink;
        bool previousOverlays = TestAppBuilder.HeadlessOptions.OverlayPopups;
        Logger.Sink = log;
        TestAppBuilder.HeadlessOptions.OverlayPopups = true;
        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateDockWindow(view, ThemeVariant.Dark, DockWidth, DockHeight);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var recent = view.FindControl<ItemsControl>("RecentThreads")!;
            Assert.That(recent.IsEffectivelyVisible, Is.True);
            Assert.That(recent.ItemCount, Is.EqualTo(3));
            AssertDockSurface(window, view, ThemeVariant.Dark);
            HeadlessCapture.SaveIfRequested(view, string.Empty, "dark-docked-empty");

            var starter = view.FindControl<ItemsControl>("StarterPrompts")!.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == AgentStrings.StarterReviewFrame);
            Click(window, starter);
            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            Assert.That(prompt.Text, Is.EqualTo(AgentStrings.StarterReviewFramePrompt));
            Assert.That(prompt.IsFocused, Is.True);
            Assert.That(viewModel.Timeline, Is.Empty);

            var permission = composer.GetVisualDescendants().OfType<AgentPermissionModePicker>().Single()
                .FindControl<ComboBox>("PermissionComboBox")!;
            permission.SelectedItem = viewModel.PermissionModes.Single(mode => mode.Mode == AgentPermissionMode.AutoAcceptEdits);
            HeadlessTestHelpers.Render(3);
            Assert.That(viewModel.SelectedPermissionMode.Mode, Is.EqualTo(AgentPermissionMode.AutoAcceptEdits));

            var providerPicker = composer.FindControl<Button>("ModelButton")!;
            var providerFlyout = (Flyout)providerPicker.Flyout!;
            Click(window, providerPicker);
            var providerRows = ((AgentModelPickerView)providerFlyout.Content!).FindControl<ItemsControl>("ModelProviderRows")!;
            var providerOption = providerRows.GetVisualDescendants().OfType<Button>().Single();
            Assert.That(AutomationProperties.GetHelpText(providerOption), Is.EqualTo(AgentStrings.ProviderAvailable));
            Click(window, providerOption);
            Assert.That(providerFlyout.IsOpen, Is.True);
            Assert.That(viewModel.ModelProviders.Single().IsSelected, Is.True);
            providerFlyout.Hide();

            var bar = view.GetVisualDescendants().OfType<AgentThreadBar>().Single();
            var switcher = bar.FindControl<Button>("ThreadSwitcherButton")!;
            var picker = (Flyout)switcher.Flyout!;
            Click(window, switcher);
            Assert.That(picker.IsOpen, Is.True);
            var search = bar.FindControl<TextBox>("ThreadSearchBox")!;
            Assert.That(search.IsFocused, Is.True);
            search.Text = "  OPENING ";
            HeadlessTestHelpers.Render(3);
            var matches = bar.FindControl<ItemsControl>("ThreadMatches")!;
            Assert.That(matches.Items.OfType<AgentThreadRow>().Select(row => row.Thread.Id), Is.EqualTo(new[] { opening.Id }));

            var rename = matches.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == AgentStrings.RenameThread);
            Click(window, rename);
            var action = (Flyout)FlyoutBase.GetAttachedFlyout(switcher)!;
            Assert.That(action.IsOpen, Is.True);
            Assert.That(picker.IsOpen, Is.False);
            var name = bar.FindControl<TextBox>("RenameTextBox")!;
            Assert.That(name.Text, Is.EqualTo(opening.Title));
            name.Text = "Opening title pass";
            var save = ((Control)action.Content!).GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, AgentStrings.Save));
            Click(window, save);
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.Threads.Any(thread => thread.Title == "Opening title pass"));
            Assert.That(viewModel.SelectedThread, Is.Null);

            Click(window, switcher);
            search.Text = "Opening title pass";
            HeadlessTestHelpers.Render(3);
            var open = matches.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Opening title pass");
            Click(window, open);
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SelectedThread?.Id == opening.Id && viewModel.CanManage);
            Assert.That(providerPicker.IsEnabled, Is.True);
            Assert.That(ToolTip.GetTip(providerPicker), Is.EqualTo(AgentStrings.ProviderLocked));

            var overflow = bar.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == AgentStrings.ThreadActions);
            Click(window, overflow);
            var menu = (MenuFlyout)overflow.Flyout!;
            var delete = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, AgentStrings.DeleteThread));
            Click(window, delete);
            Assert.That(action.IsOpen, Is.True);
            var cancel = ((Control)action.Content!).GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, AgentStrings.Cancel));
            Click(window, cancel);
            Assert.That(action.IsOpen, Is.False);
            Assert.That(viewModel.SelectedThread?.Id, Is.EqualTo(opening.Id));
            Assert.That(log.Messages, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previousOverlays;
            Logger.Sink = previousLog;
        }
    }

    [AvaloniaTest]
    public Task DarkThemeRendersStreamingAndCompletedTurns() => ExerciseTurnAsync(ThemeVariant.Dark);

    [AvaloniaTest]
    public Task LightThemeRendersCompletedTurns() => ExerciseTurnAsync(ThemeVariant.Light);

    private static async Task ExerciseTurnAsync(ThemeVariant variant)
    {
        using var theme = UseDockTheme(variant);
        await using var workspace = new AgentTabTestWorkspace { TransactionCount = 2, DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        bool dark = variant == ThemeVariant.Dark;
        string pngPath = Path.Combine(workspace.Root, "frame.png");
        string pngData = CreatePreview(pngPath);
        var image = dark ? new AgentToolOutput("image", Data: pngData) : new AgentToolOutput("image", Uri: new Uri(pngPath).AbsoluteUri);
        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now, "Check the framing and title at the playhead."));
            workspace.Bridge.Emit(new AgentPlanUpdated(thread, turn, now,
                [new AgentPlanStep("Review the opening frame", AgentPlanStepStatus.Completed),
                 new AgentPlanStep("Set the title fade", AgentPlanStepStatus.InProgress)]));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "preview", "render_still", "beutl",
                JsonSerializer.SerializeToElement(new { time = 0.5 })));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "preview", AgentToolStatus.Completed,
                [image, new AgentToolOutput("text", "Frame at 00:00.500")]));
            workspace.Bridge.Emit(new AgentAssistantTextDelta(thread, turn, now, ResponseIntro));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "save", "save_preset", "beutl"));
            await workspace.Bridge.AskPermissionAsync(request, "Save the title preset", null, ct, "Presets / Opening title");
            await workspace.Bridge.AskQuestionAsync(request,
                [new AgentQuestion("fade", "How long should the title fade last?",
                    [new("1 second", "A quick fade for a sharp opening."), new("2 seconds", "A slower entrance over the opening shot.")],
                    Header: "Transition")], ct);
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "save", AgentToolStatus.Completed,
                [new AgentToolOutput("text", "Saved Opening title")]));
            workspace.Bridge.Emit(new AgentPlanUpdated(thread, turn, now,
                [new AgentPlanStep("Review the opening frame", AgentPlanStepStatus.Completed),
                 new AgentPlanStep("Set the title fade", AgentPlanStepStatus.Completed)]));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, Response));
        };
        var log = new UiLog();
        ILogSink? previousLog = Logger.Sink;
        Logger.Sink = log;
        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateDockWindow(view, variant, DockWidth, DockHeight);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = view.GetVisualDescendants().OfType<AgentComposerView>().Single();
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            prompt.Text = "Refine the opening title.";
            prompt.CaretIndex = prompt.Text.Length;
            prompt.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
            Assert.That(prompt.Text, Does.Contain("\n"));
            Assert.That(viewModel.IsRunning, Is.False);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActivePermission);
            HeadlessTestHelpers.Render(4);
            var markdown = await WaitForMarkdownAsync(view);
            var thumbnail = view.GetVisualDescendants().OfType<Image>().Single(control => control.Source is not null);
            Assert.That(thumbnail.IsEffectivelyVisible, Is.True);
            var tool = view.GetVisualDescendants().OfType<ToggleButton>()
                .Single(toggle => toggle.DataContext is AgentToolItem { ToolName: "render_still" });
            Assert.That(tool.IsChecked, Is.Not.True);
            Assert.That(composer.IsEffectivelyVisible, Is.False);
            Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>(), Is.Empty);
            Assert.That(VisibleAction(view, AgentStrings.Stop), Is.Not.Null);
            Assert.That(VisibleAction(view, AgentStrings.CopyResponse), Is.Null);

            Click(window, tool);
            Assert.That(view.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Any(text => text.Text == "{\"time\":0.5}" && text.IsEffectivelyVisible), Is.True);
            Click(window, tool);

            var approval = view.FindControl<AgentPermissionCard>("ActivePermissionCard")!;
            Assert.That(approval.IsEffectivelyVisible, Is.True);
            Assert.That(Grid.GetRow(approval), Is.EqualTo(Grid.GetRow(composer)));
            Assert.That(approval.DataContext, Is.SameAs(viewModel.ActivePermission));
            Assert.That(view.GetVisualDescendants().OfType<AgentPermissionCard>().Single(), Is.SameAs(approval));
            var allow = approval.FindControl<Button>("SubmitPermission")!;
            Click(window, allow);
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActiveQuestion);
            HeadlessTestHelpers.Render(4);
            Assert.That(viewModel.HasActivePermission, Is.False);
            Assert.That(approval.IsEffectivelyVisible, Is.False);
            Assert.That(composer.IsEffectivelyVisible, Is.False);
            Assert.That(VisibleAction(view, AgentStrings.Stop), Is.Not.Null);
            Assert.That(viewModel.Timeline.OfType<AgentQuestionItem>(), Is.Empty);
            var questionCard = view.FindControl<AgentQuestionCard>("ActiveQuestionCard")!;
            var questionControl = questionCard.FindControl<ListBox>("SingleOptions")!;
            Assert.That(questionControl.IsEffectivelyVisible, Is.True);
            Assert.That(questionControl.IsKeyboardFocusWithin, Is.True);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.That(questionControl.SelectedIndex, Is.EqualTo(1));
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.That(questionControl.SelectedIndex, Is.Zero);
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
            Assert.That(questionControl.SelectedIndex, Is.EqualTo(1));
            HeadlessTestHelpers.Render(4);
            if (variant == ThemeVariant.Dark)
                HeadlessCapture.SaveIfRequested(view, string.Empty, "question-single-select");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await HeadlessTestHelpers.WaitUntilAsync(() => !viewModel.IsRunning);
            await HeadlessTestHelpers.WaitUntilAsync(() => view.GetVisualDescendants().OfType<Border>().Any(border => border.Classes.Contains("Table")));
            HeadlessTestHelpers.Render(4);

            var rows = view.GetVisualDescendants().OfType<Grid>().Where(grid => grid.Classes.Contains("agent-resolution") && grid.IsEffectivelyVisible).ToArray();
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows.Select(row => row.Bounds.Height), Is.All.LessThanOrEqualTo(24));
            Assert.That(rows.Select(row => row.Children.OfType<TextBlock>().Single().Text), Is.EqualTo(new[]
            {
                $"{AgentStrings.AllowOnce} · Save the title preset",
                $"{AgentStrings.Answered} · 2 seconds"
            }));
            Assert.That(approval.IsEffectivelyVisible, Is.False);
            Assert.That(questionControl.IsEffectivelyVisible, Is.False);
            Assert.That(composer.IsEffectivelyVisible, Is.True);
            Assert.That(VisibleAction(view, AgentStrings.RevertTurn), Is.Not.Null);
            Assert.That(markdown.DocumentUpdate, Is.Not.Null);
            Assert.That(prompt.IsReadOnly, Is.False);
            AssertDockSurface(window, view, variant);

            var copy = VisibleAction(view, AgentStrings.CopyResponse)!;
            Click(window, copy);
            Assert.That(await window.Clipboard!.TryGetTextAsync(), Is.EqualTo(Response));
            Assert.That(log.Messages, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            Logger.Sink = previousLog;
        }
    }

    [AvaloniaTest]
    public async Task QuestionCardStepsThroughOtherMultiSelectAndSecretAnswers()
    {
        using var theme = UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        IReadOnlyDictionary<string, string[]>? answers = null;
        workspace.Bridge.Script = async (request, ct) => answers = await workspace.Bridge.AskQuestionAsync(request,
        [
            new("tone", "What tone should the trailer have?", [new("Warm"), new("Cool")], Header: "Tone"),
            new("sections", "Which sections should the trailer include?",
                [new("Opening title", "Introduce the project before the montage."), new("Montage", "Use a sequence of short shots."),
                 new("End card", "Close with the title and credits.")], Header: "Sections", MultiSelect: true),
            new("secret", "Enter the access code for the review file.", IsSecret: true, Header: "Access")
        ], ct);

        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateDockWindow(view, ThemeVariant.Dark, DockWidth, DockHeight);
        try
        {
            window.Show();
            viewModel.Prompt = "Prepare a trailer with the selected sections.";
            var send = viewModel.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActiveQuestion);
            HeadlessTestHelpers.Render(4);
            var card = view.FindControl<AgentQuestionCard>("ActiveQuestionCard")!;
            var step = card.FindControl<TextBlock>("QuestionStep")!;
            var textbox = card.FindControl<TextBox>("AnswerBox")!;
            Assert.That(step.Text, Is.EqualTo("1 / 3"));
            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.That(textbox.IsFocused, Is.True);
            window.KeyTextInput("Neutral");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestHelpers.Render(4);
            Assert.That(step.Text, Is.EqualTo("2 / 3"));

            Click(window, card.FindControl<Button>("QuestionBack")!);
            Assert.That(step.Text, Is.EqualTo("1 / 3"));
            Assert.That(textbox.Text, Is.EqualTo("Neutral"));
            Click(window, card.FindControl<Button>("QuestionAdvance")!);
            var choices = card.FindControl<ItemsControl>("MultiOptions")!.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Click(window, choices[0]);
            Click(window, choices[2]);
            Click(window, card.FindControl<CheckBox>("MultiOther")!);
            Click(window, textbox);
            window.KeyTextInput("Behind the scenes");
            HeadlessTestHelpers.Render(4);
            var backButton = card.FindControl<Button>("QuestionBack")!;
            var skipButton = card.FindControl<Button>("QuestionSkip")!;
            var advanceButton = card.FindControl<Button>("QuestionAdvance")!;
            Assert.That(skipButton.IsEffectivelyVisible, Is.True);
            Assert.That(backButton.TranslatePoint(default, window)!.Value.X, Is.LessThan(skipButton.TranslatePoint(default, window)!.Value.X));
            Assert.That(skipButton.TranslatePoint(default, window)!.Value.X, Is.LessThan(advanceButton.TranslatePoint(default, window)!.Value.X));
            HeadlessCapture.SaveIfRequested(view, string.Empty, "question-multi-select");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            HeadlessTestHelpers.Render(4);
            Assert.That(step.Text, Is.EqualTo("3 / 3"));
            Assert.That(textbox.IsFocused, Is.True);
            Assert.That(textbox.PasswordChar, Is.EqualTo('•'));
            window.KeyTextInput("review-code");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await send.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(answers!["tone"], Is.EqualTo(new[] { "Neutral" }));
                Assert.That(answers!["sections"], Is.EqualTo(new[] { "Opening title", "End card", "Behind the scenes" }));
                Assert.That(answers!["secret"], Is.EqualTo(new[] { "review-code" }));
                Assert.That(viewModel.Timeline.OfType<AgentQuestionItem>().Single().ResolutionText, Does.Not.Contain("review-code"));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task QuestionSkipOmitsSkippedStepAndSubmitsRemainingAnswers()
    {
        using var theme = UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        IReadOnlyDictionary<string, string[]>? answers = null;
        workspace.Bridge.Script = async (request, ct) =>
        {
            answers = await workspace.Bridge.AskQuestionAsync(request,
            [
                new("tone", "Tone?", [new("Warm"), new("Cool")], Header: "Tone"),
                new("notes", "Optional notes?", Header: "Notes")
            ], ct);
        };

        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateDockWindow(view, ThemeVariant.Dark, DockWidth, DockHeight);
        try
        {
            window.Show();
            viewModel.Prompt = "Ask two questions.";
            var send = viewModel.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActiveQuestion);
            HeadlessTestHelpers.Render(4);
            var card = view.FindControl<AgentQuestionCard>("ActiveQuestionCard")!;
            Click(window, card.FindControl<Button>("QuestionAdvance")!);
            HeadlessTestHelpers.Render(4);
            Click(window, card.FindControl<Button>("QuestionSkip")!);
            await send.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(answers, Is.Not.Null);
            Assert.That(answers!.ContainsKey("tone"), Is.True);
            Assert.That(answers["tone"], Is.EqualTo(new[] { "Warm" }));
            Assert.That(answers.ContainsKey("notes"), Is.False);
            Assert.That(viewModel.HasActiveQuestion, Is.False);
            Assert.That(view.FindControl<AgentComposerView>("Composer")!.IsEffectivelyVisible, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task StopFromThreadBarClosesPendingQuestionAndRestoresComposer()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        workspace.Bridge.Script = (request, ct) => workspace.Bridge.AskQuestionAsync(request, [new("notes", "Any notes?")], ct);
        var view = new AgentTabView { DataContext = viewModel };
        var window = CreateDockWindow(view, ThemeVariant.Dark, DockWidth, DockHeight);
        try
        {
            window.Show();
            viewModel.Prompt = "Ask for review notes.";
            var send = viewModel.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActiveQuestion);
            HeadlessTestHelpers.Render(4);
            Click(window, VisibleAction(view, AgentStrings.Stop)!);
            await send.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(view.FindControl<AgentComposerView>("Composer")!.IsEffectivelyVisible, Is.True);
            Assert.That(viewModel.Timeline.OfType<AgentQuestionItem>().Single().ResolutionText, Is.EqualTo(AgentStrings.RequestClosed));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    internal static IDisposable UseDockTheme(ThemeVariant variant)
    {
        var theme = ThemeVariantScope.Use(variant);
        var dictionaries = Application.Current!.Resources.ThemeDictionaries;
        // TestApp adds the default Dark Border theme. Use the classic Dark dock for these captures.
        if (variant != ThemeVariant.Dark || !dictionaries.Remove(ThemeVariant.Dark, out var darkBorder))
            return theme;
        return Disposable.Create(() =>
        {
            dictionaries.Add(ThemeVariant.Dark, darkBorder);
            theme.Dispose();
        });
    }

    internal static Window CreateDockWindow(AgentTabView view, ThemeVariant variant, int width, int height)
    {
        var panel = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(8, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = view,
        };
        panel.Bind(Border.BackgroundProperty, panel.GetResourceObservable("DockSurfacePanelBrush"));
        panel.Bind(Border.BorderBrushProperty, panel.GetResourceObservable("DockBorderSubtleBrush"));
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = panel };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("DockSurfaceWorkbenchBrush"));
        return window;
    }

    private static void AssertDockSurface(Window window, AgentTabView view, ThemeVariant variant)
    {
        Assert.That(view.ActualThemeVariant, Is.EqualTo(variant));
        Assert.That(((Grid)view.Content!).Background, Is.Null);
        var panel = (Border)window.Content!;
        var brush = (ISolidColorBrush)panel.Background!;
        Assert.That(brush.Color, Is.EqualTo(variant == ThemeVariant.Dark
            ? Color.Parse("#1A1A1A")
            : (Color)panel.FindResource("LayerFillColorDefault")!));

        using var frame = window.CaptureRenderedFrame()!;
        var origin = view.TranslatePoint(default, window)!.Value;
        int x = (int)origin.X;
        int y = (int)origin.Y + 50;
        Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(x + 4, y)),
            Is.EqualTo(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(x - 4, y))));
    }

    private static async Task<MarkdownRenderer> WaitForMarkdownAsync(AgentTabView view)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => view.GetVisualDescendants().OfType<MarkdownRenderer>()
            .Any(renderer => renderer.DocumentUpdate is not null));
        return view.GetVisualDescendants().OfType<MarkdownRenderer>().Single();
    }

    private static Button? VisibleAction(Control view, string name) => view.GetVisualDescendants().OfType<Button>()
        .SingleOrDefault(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name);

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.That(point, Is.Not.Null);
        window.MouseDown(point!.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
    }

    internal static string CreatePreview(string path)
    {
        using var bitmap = new SKBitmap(256, 77);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(0.8f);
        using var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 96),
            [SKColor.Parse("#26324a"), SKColor.Parse("#5f5662"), SKColor.Parse("#d09972")], null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = sky };
        canvas.DrawRect(0, 0, 320, 97, paint);
        paint.Shader = null;
        paint.Color = SKColor.Parse("#ecc091");
        canvas.DrawCircle(247, 33, 13, paint);

        using var hillBuilder = new SKPathBuilder();
        hillBuilder.MoveTo(0, 73);
        hillBuilder.LineTo(65, 43);
        hillBuilder.LineTo(132, 77);
        hillBuilder.LineTo(185, 58);
        hillBuilder.LineTo(225, 77);
        hillBuilder.LineTo(320, 49);
        hillBuilder.LineTo(320, 96);
        hillBuilder.LineTo(0, 96);
        hillBuilder.Close();
        using var hills = hillBuilder.Detach();
        paint.Color = SKColor.Parse("#283444");
        canvas.DrawPath(hills, paint);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 22);
        paint.Color = SKColor.Parse("#f8efe1");
        canvas.DrawText("FIELD NOTES", 160, 54, SKTextAlign.Center, font, paint);
        canvas.DrawRect(146, 64, 28, 1, paint);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] data = encoded.ToArray();
        File.WriteAllBytes(path, data);
        return Convert.ToBase64String(data);
    }

    private sealed class UiLog : ILogSink
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Layout;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (IsEnabled(level, area))
                Messages.Enqueue(messageTemplate);
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            if (IsEnabled(level, area))
                Messages.Enqueue(messageTemplate + " " + string.Join(" | ", propertyValues));
        }
    }
}
