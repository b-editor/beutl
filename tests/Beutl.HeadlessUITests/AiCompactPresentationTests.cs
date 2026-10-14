using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiCompactPresentationTests
{
    [AvaloniaTest]
    public void UsageSummary_CollapsesWithoutASnapshotAndReportsTheShareAsTextAlone()
    {
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        using var usage = new AiUsageViewModel(entitlements);
        var view = new AiUsageSummaryView
        {
            DataContext = usage,
            VerticalAlignment = VerticalAlignment.Top,
        };
        using var shown = ShowInWindow(view, 340, 400);

        Assert.That(view.Bounds.Height, Is.Zero,
            "Without an entitlement snapshot there is no usage to report.");

        entitlements.Value = new AiEntitlements(
            "pro",
            "active",
            null,
            null,
            false,
            true,
            new AiBalance(new AiMonthlyUsage(25, 75, false), 40, false),
            new AiOperationAvailability([]));
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(view.Bounds.Height, Is.GreaterThan(0));
            Assert.That(view.Bounds.Height, Is.LessThanOrEqualTo(24),
                "The usage readout must stay one caption row.");
            Assert.That(
                view.GetLogicalDescendants().OfType<ProgressBar>(),
                Is.Empty,
                "The share is already in the text; a meter only repeats it.");
            Assert.That(
                view.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.FontSize),
                Is.All.LessThanOrEqualTo(11),
                "Usage is secondary information and stays at the compact size.");
            Assert.That(
                ToolTip.GetTip(view.GetLogicalDescendants().OfType<StackPanel>().First()),
                Is.EqualTo(usage.MonthlyRemainingText.Value),
                "The remaining share moved into the tooltip rather than costing a line.");
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_GivesTheAspectRatioAndModelARowEach()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 480, 640);

        Control aspectRatio = view.FindControl<Control>("AspectRatioField")!;
        Control model = view.FindControl<Control>("ModelField")!;

        viewModel.ModelPicker.HasChoice.Value = true;
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.IsVisible, Is.True);
            Assert.That(aspectRatio.Bounds.Bottom, Is.LessThanOrEqualTo(model.Bounds.Top + 1),
                "One selector per row, so neither is trimmed to half a docked tab.");
            Assert.That(
                new[] { aspectRatio, model }.Select(field => field.Bounds.Width),
                Is.All.EqualTo(aspectRatio.Bounds.Width),
                "Each selector gets the full width.");
        }

        viewModel.ModelPicker.HasChoice.Value = false;
        HeadlessTestHelpers.Render();

        Assert.That(model.IsVisible, Is.False,
            "A single model on offer is not a choice, so its row goes away.");
    }

    [AvaloniaTest]
    public async Task ImageEdit_SwitchesTasksThroughADropDown()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageEditDialogViewModel viewModel = CreateImageEditDialog(clients);
        var view = new AiImageEditView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 480, 640);

        ComboBox picker = view.FindControl<ComboBox>("TaskPicker")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(picker.ItemCount, Is.EqualTo(viewModel.Tasks.Count));
            Assert.That(picker.SelectedItem, Is.SameAs(viewModel.SelectedTask.Value));
            Assert.That(picker.Bounds.Height, Is.LessThanOrEqualTo(40),
                "A collapsed drop-down keeps the task list to one row in a narrow dock pane.");
        }

        picker.SelectedItem = viewModel.Tasks[2];
        HeadlessTestHelpers.Render();

        Assert.That(viewModel.SelectedTask.Value, Is.SameAs(viewModel.Tasks[2]));
    }

    [AvaloniaTest]
    public async Task ImageEdit_ClearingAnErrorKeepsAMacSafeLiveRegion()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageEditDialogViewModel viewModel = CreateImageEditDialog(clients);
        var view = new AiImageEditView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 480, 640);
        TextBlock error = view.FindControl<TextBlock>("ImageEditErrorMessage")!;
        AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(error);
        Assert.That(AutomationProperties.GetLiveSetting(error), Is.EqualTo(AutomationLiveSetting.Assertive));

        viewModel.Error.Value = "A test error";
        HeadlessTestHelpers.Render();
        Assert.That(peer.GetName(), Is.EqualTo("A test error"));

        viewModel.Error.Value = null;
        HeadlessTestHelpers.Render();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.IsVisible, Is.False);
            Assert.That(error.Text, Is.Null);
            Assert.That(peer.GetName(),
                Is.EqualTo(OperatingSystem.IsMacOS() ? "\u200B" : string.Empty));
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_StacksTheSelectorsFromCoarsestToFinest()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 460, 900);
        viewModel.ModelPicker.HasChoice.Value = true;
        HeadlessTestHelpers.Render();

        Control model = view.FindControl<Control>("ModelField")!;
        Control duration = view.FindControl<Control>("DurationField")!;
        Control resolution = view.FindControl<Control>("ResolutionField")!;
        Control aspectRatio = view.FindControl<Control>("AspectRatioField")!;
        double modelTop = TopIn(view, model);
        double durationTop = TopIn(view, duration);
        double resolutionTop = TopIn(view, resolution);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(durationTop, Is.GreaterThanOrEqualTo(modelTop + model.Bounds.Height),
                "The model comes first: it decides what the rest of the run costs.");
            Assert.That(resolutionTop, Is.GreaterThanOrEqualTo(durationTop + duration.Bounds.Height));
            Assert.That(TopIn(view, aspectRatio), Is.EqualTo(resolutionTop).Within(1),
                "Resolution and aspect ratio describe the same frame, so they share a row.");
            Assert.That(model.Bounds.Width, Is.EqualTo(duration.Bounds.Width).Within(1));
            Assert.That(resolution.Bounds.Width, Is.LessThan(duration.Bounds.Width));
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_EmptyCueListDoesNotReserveAScreenful()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        viewModel.SelectedSubtitlePageIndex.Value = 1;
        var view = new AiSubtitleView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 460, 900);

        ListBox cues = view.FindControl<ListBox>("CaptionCueList")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Cues, Is.Empty);
            Assert.That(cues.IsEffectivelyVisible, Is.False,
                "An empty cue list is a sentence, not an empty box.");
            Assert.That(
                view.GetLogicalDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == Strings.AiSubtitle_CueListEmpty
                        && text.IsEffectivelyVisible),
                Is.True,
                "It says what would put cues there.");
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_LeadsWithTheTemplatesAndKeepsTheDetailsBelowTheOptions()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 420, 1200);

        Expander templates = FindExpander(view, Strings.AiPromptTemplates);
        Expander details = FindExpander(view, Strings.AiPromptDetails);
        Expander reference = FindExpander(view, Strings.AiReferenceImageHeader);
        TextBox prompt = view.GetLogicalDescendants().OfType<TextBox>().First(box => box.AcceptsReturn);
        ComboBox background = view.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(box => AutomationProperties.GetName(box) == Strings.AiBackground);
        Button generate = view.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, viewModel.Generate));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TopIn(view, templates), Is.LessThan(TopIn(view, prompt)),
                "A saved prompt is picked before one is written.");
            Assert.That(
                TopIn(view, details),
                Is.GreaterThan(TopIn(view, background) + background.Bounds.Height),
                "The details are a refinement, so they sit under the choices they refine.");
            Assert.That(
                TopIn(view, reference),
                Is.GreaterThan(TopIn(view, details)),
                "A picture to work from is the last thing added, under everything written by hand.");
            Assert.That(generate.Classes, Does.Contain("accent"));
            Assert.That(
                generate.Bounds.Width,
                Is.GreaterThan(view.Bounds.Width - 40),
                "The run button takes the row: it is what the tab is for.");
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_KeepsTheDetailsAndFrameGuidanceBelowTheOptions()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 460, 1400);

        Expander templates = FindExpander(view, Strings.AiPromptTemplates);
        Expander details = FindExpander(view, Strings.AiPromptDetails);
        Expander frames = FindExpander(view, Strings.AiVideoFrameGuidanceHeader);
        TextBox prompt = view.GetLogicalDescendants().OfType<TextBox>().First(box => box.AcceptsReturn);
        CheckBox generateAudio = view.GetLogicalDescendants().OfType<CheckBox>()
            .Single(box => Equals(box.Content, Strings.AiGenerateAudio));
        Button generate = view.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, viewModel.Generate));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TopIn(view, templates), Is.LessThan(TopIn(view, prompt)));
            Assert.That(
                TopIn(view, details),
                Is.GreaterThan(TopIn(view, generateAudio) + generateAudio.Bounds.Height));
            Assert.That(TopIn(view, frames), Is.GreaterThan(TopIn(view, details)));
            Assert.That(generate.Classes, Does.Contain("accent"));
            Assert.That(generate.Bounds.Width, Is.GreaterThan(view.Bounds.Width - 40));
        }
    }

    [AvaloniaTest]
    public async Task PromptDetails_LabelEveryFieldItAsksFor()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 420, 900);
        Expander details = view.GetLogicalDescendants()
            .OfType<Expander>()
            .Single(expander => Equals(expander.Header, Strings.AiPromptDetails));
        details.IsExpanded = true;
        HeadlessTestHelpers.Render();

        List<string> labels = details.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .ToList();
        int fieldCount = details.GetLogicalDescendants().OfType<TextBox>().Count()
            + details.GetLogicalDescendants().OfType<NumericUpDown>().Count();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(labels, Does.Contain(Strings.AiPromptStyle));
            Assert.That(labels, Does.Contain(Strings.AiPromptComposition));
            Assert.That(labels, Does.Contain(Strings.AiPromptAvoid));
            Assert.That(labels, Does.Contain(Strings.AiPromptSeed),
                "The seed is what makes a result reproducible, so it is offered here.");
            Assert.That(fieldCount, Is.EqualTo(4),
                "A watermark disappears as soon as there is text, so every field carries a label.");
        }
    }

    [AvaloniaTest]
    public async Task AiTabs_OfferThePlanOnlyWhereThereIsNoPlanYet()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 420, 900);

        List<Button> planButtons = view.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => ReferenceEquals(button.Command, viewModel.OpenAiPlan))
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(planButtons, Has.Count.EqualTo(1),
                "Buying more is a settings errand, not a button beside every run.");
            Assert.That(planButtons[0].Content, Is.EqualTo(Strings.AiJoinPro));
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_SetsTheLengthWithASliderThatStopsOnWhatTheModelTakes()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 460, 900);

        Slider slider = view.FindControl<Slider>("DurationSlider")!;
        TextBlock value = view.FindControl<TextBlock>("DurationValue")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(slider.Minimum, Is.Zero);
            Assert.That(slider.Maximum, Is.EqualTo(viewModel.DurationOptions.Count - 1),
                "The scale is the list of lengths, so it cannot land between them.");
            Assert.That(slider.IsSnapToTickEnabled, Is.True);
            Assert.That(slider.Value, Is.EqualTo(1));
            Assert.That(value.Text, Is.EqualTo(viewModel.SelectedDuration.Value.ToString()));
        }

        slider.Value = 2;
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.SelectedDuration.Value.Seconds, Is.EqualTo(8));
            Assert.That(value.Text, Is.EqualTo(viewModel.SelectedDuration.Value.ToString()),
                "The chosen length is spelled out: a slider position is not a number of seconds.");
        }

        viewModel.SelectedDuration.Value = viewModel.DurationOptions.Single(option => option.Seconds == 4);
        HeadlessTestHelpers.Render();

        Assert.That(slider.Value, Is.Zero, "The slider follows a length chosen anywhere else.");
    }

    [AvaloniaTest]
    public async Task ImageGeneration_SeedFieldCarriesTheValueBothWays()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 420, 1200);
        FindExpander(view, Strings.AiPromptDetails).IsExpanded = true;
        HeadlessTestHelpers.Render();

        AssertSeedFieldCarriesTheValueBothWays(view, viewModel.Seed);
    }

    [AvaloniaTest]
    public async Task VideoGeneration_SeedFieldCarriesTheValueBothWays()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 460, 1400);
        FindExpander(view, Strings.AiPromptDetails).IsExpanded = true;
        HeadlessTestHelpers.Render();

        AssertSeedFieldCarriesTheValueBothWays(view, viewModel.Seed);
    }

    // A seed is only worth offering if the number typed in is the number sent,
    // which is a decimal in the field and an integer in the request.
    private static void AssertSeedFieldCarriesTheValueBothWays(Visual view, ReactivePropertySlim<int?> seed)
    {
        NumericUpDown field = view.GetLogicalDescendants().OfType<NumericUpDown>().Single();

        field.Value = 4242m;
        HeadlessTestHelpers.Render();
        Assert.That(seed.Value, Is.EqualTo(4242));

        seed.Value = 7;
        HeadlessTestHelpers.Render();
        Assert.That(field.Value, Is.EqualTo(7m));

        seed.Value = null;
        HeadlessTestHelpers.Render();
        Assert.That(field.Value, Is.Null, "Nothing typed in means the server picks the seed.");
    }

    private static Expander FindExpander(Visual view, string header)
        => view.GetLogicalDescendants().OfType<Expander>().Single(expander => Equals(expander.Header, header));

    private static double TopIn(Visual root, Visual control)
        => control.TranslatePoint(default, root)?.Y ?? double.NaN;

    // Shows view in a window of the given size and renders it; disposing closes the window.
    private static IDisposable ShowInWindow(Control view, double width, double height)
    {
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        HeadlessTestHelpers.Render();
        return Disposable.Create(() =>
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        });
    }

    [AvaloniaTest]
    public async Task ImageGeneration_SaysNothingIsWrongUntilSomethingHasBeenTyped()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 420, 900);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.PromptValidationError.Value, Is.EqualTo(Strings.AiPromptRequired),
                "The request still cannot be sent.");
            Assert.That(viewModel.VisiblePromptValidationError.Value, Is.Null,
                "But a tab nobody has typed in yet has not made a mistake.");
            Assert.That(FindValidationText(view)?.IsVisible ?? false, Is.False);
        }

        viewModel.Prompt.Value = "a calm sunset";
        HeadlessTestHelpers.Render();
        Assert.That(viewModel.VisiblePromptValidationError.Value, Is.Null);

        viewModel.Prompt.Value = string.Empty;
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.VisiblePromptValidationError.Value,
                Is.EqualTo(Strings.AiPromptRequired),
                "Once the box has been used, emptying it is worth pointing out.");
            Assert.That(FindValidationText(view)?.IsVisible ?? false, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_HoldsTheValidationBackTheSameWay()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.PromptValidationError.Value, Is.EqualTo(Strings.AiPromptRequired));
            Assert.That(viewModel.VisiblePromptValidationError.Value, Is.Null,
                "An untouched tab has not made a mistake.");
        }

        // A detail alone still composes into a prompt, so the complaint waits for
        // the box to be emptied again rather than for the main prompt specifically.
        viewModel.Motion.Value = "slow push-in";
        Assert.That(viewModel.VisiblePromptValidationError.Value, Is.Null);

        viewModel.Motion.Value = string.Empty;
        Assert.That(viewModel.VisiblePromptValidationError.Value, Is.EqualTo(Strings.AiPromptRequired));
    }

    [AvaloniaTest]
    public async Task ImageEdit_HoldsTheValidationBackUntilThePromptIsUsed()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageEditDialogViewModel viewModel = CreateImageEditDialog(clients);
        viewModel.SelectedTask.Value = viewModel.Tasks.First(task => task.Value == "restyle");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.PromptValidationError.Value, Is.EqualTo(Strings.AiPromptRequired));
            Assert.That(viewModel.VisiblePromptValidationError.Value, Is.Null);
        }

        viewModel.Prompt.Value = "watercolour";
        viewModel.Prompt.Value = string.Empty;

        Assert.That(viewModel.VisiblePromptValidationError.Value, Is.EqualTo(Strings.AiPromptRequired));
    }

    [AvaloniaTest]
    public async Task ImageGeneration_KeepsTheResultBoxAndItsActionsAwayUntilThereIsAResult()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 300, 560);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                view.GetLogicalDescendants().OfType<Border>()
                    .Any(border => border.IsVisible && border.Height == 320),
                Is.False,
                "An empty 320px box would be most of a docked tab.");
            Assert.That(
                view.GetLogicalDescendants().OfType<TextBlock>()
                    .Any(text => text.IsVisible && text.Text == Strings.AiImageGenerationIdle),
                Is.True,
                "What stands there instead says what to do next.");
            Assert.That(
                view.GetLogicalDescendants().OfType<Button>()
                    .Any(button => button.IsEffectivelyVisible
                        && Equals(button.Content, Strings.AiAddToScene)),
                Is.False,
                "There is nothing to add to the scene yet.");
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_OffersAWayOutWhileTheRequestRuns()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 420, 900);

        Button cancel = view.GetLogicalDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Strings.Cancel));
        Assert.That(cancel.IsVisible, Is.False, "Nothing is running yet.");

        viewModel.IsGenerating.Value = true;
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancel.IsVisible, Is.True,
                "Generation runs for as long as the server takes; leaving must not mean closing the tab.");
            Assert.That(viewModel.StopGenerating.CanExecute(), Is.True);
        }

        viewModel.IsGenerating.Value = false;
        HeadlessTestHelpers.Render();
        Assert.That(viewModel.StopGenerating.CanExecute(), Is.False);
    }

    [AvaloniaTest]
    public async Task Subtitle_ShowsProgressAndOffersOneStopForTranscriptionAndTranslation()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        var view = new AiSubtitleView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 340, 900);
        FAProgressRing progress = view.FindControl<FAProgressRing>("TranscriptionProgressRing")!;
        TextBlock status = view.FindControl<TextBlock>("TranscriptionStatusText")!;
        AutomationPeer statusPeer = ControlAutomationPeer.CreatePeerForElement(status);
        var automationChanges = new List<AutomationPropertyChangedEventArgs>();
        statusPeer.PropertyChanged += (_, args) => automationChanges.Add(args);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.StopRequest.CanExecute(), Is.False);
            Assert.That(progress.IsEffectivelyVisible, Is.False);
            Assert.That(status.IsEffectivelyVisible, Is.True);
            Assert.That(status.Text, Is.Empty);
            Assert.That(viewModel.TranscriptionStatusText.Value, Is.Empty);
        }

        viewModel.IsTranscribing.Value = true;
        HeadlessTestHelpers.Render();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.StopRequest.CanExecute(), Is.True);
            Assert.That(progress.IsEffectivelyVisible, Is.True);
            Assert.That(progress.IsIndeterminate, Is.True);
            Assert.That(status.IsEffectivelyVisible, Is.True);
            Assert.That(status.Text, Is.EqualTo(Strings.AiSubtitle_Transcribing));
            Assert.That(
                viewModel.TranscriptionStatusText.Value,
                Is.EqualTo(Strings.AiSubtitle_Transcribing));
            Assert.That(
                AutomationProperties.GetName(progress),
                Is.EqualTo(Strings.AiSubtitle_Transcribing));
            Assert.That(
                AutomationProperties.GetLiveSetting(status),
                Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(
                automationChanges.Any(change =>
                    ReferenceEquals(
                        change.Property,
                        AutomationElementIdentifiers.NameProperty)
                    && Equals(change.NewValue, Strings.AiSubtitle_Transcribing)),
                Is.True);
        }

        viewModel.IsTranscribing.Value = false;
        HeadlessTestHelpers.Render();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.StopRequest.CanExecute(), Is.False);
            Assert.That(progress.IsEffectivelyVisible, Is.False);
            Assert.That(status.IsEffectivelyVisible, Is.True);
            Assert.That(status.Text, Is.Empty);
            Assert.That(viewModel.TranscriptionStatusText.Value, Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_SeparatesTranscribeEditAndTranslateWorkflows()
    {
        await TestReset.ResetShellAsync();
        await using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        var view = new AiSubtitleView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 180, 900);
        TabStrip workflow = view.FindControl<TabStrip>("SubtitleWorkflowTabs")!;
        Button stop = view.FindControl<Button>("SubtitleStopButton")!;
        ScrollViewer scroll = (ScrollViewer)view.Content!;
        TabStripItem[] workflowItems = workflow.GetVisualDescendants()
            .OfType<TabStripItem>()
            .ToArray();
        ComboBox source = FindByAutomationName<ComboBox>(view, Strings.AiSubtitle_AudioSource);
        ComboBox template = view.FindControl<ComboBox>("CaptionTemplateComboBox")!;
        ComboBox target = FindByAutomationName<ComboBox>(view, Strings.AiSubtitle_TargetLanguage);
        ListBox cues = view.FindControl<ListBox>("CaptionCueList")!;
        TextBlock transcribeDescription = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Text == Strings.AiSubtitle_TranscribeDescription);
        TextBlock editDescription = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Text == Strings.AiSubtitle_EditDescription);
        TextBlock translateDescription = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Text == Strings.AiSubtitle_TranslateDescription);
        double[] workflowItemTops = workflowItems
            .Select(item => item.TranslatePoint(default, workflow)?.Y ?? double.PositiveInfinity)
            .ToArray();
        double rightmostWorkflowItem = workflowItems.Max(item =>
            item.TranslatePoint(new Point(item.Bounds.Width, 0), workflow)?.X
            ?? double.PositiveInfinity);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workflowItems.Select(AutomationProperties.GetName),
                Is.EqualTo(new[]
                {
                    Strings.AiSubtitle_Transcribe,
                    Strings.AiSubtitle_Edit,
                    Strings.AiSubtitle_Translate,
                }));
            Assert.That(workflowItemTops.Select(value => Math.Round(value)).Distinct().Count(),
                Is.GreaterThan(1),
                "The workflow choices wrap instead of clipping in a 180px dock.");
            Assert.That(rightmostWorkflowItem, Is.LessThanOrEqualTo(workflow.Bounds.Width + 1));
            Assert.That(scroll.Extent.Width, Is.LessThanOrEqualTo(scroll.Viewport.Width + 1));
            Assert.That(viewModel.SelectedSubtitlePageIndex.Value, Is.Zero);
            Assert.That(source.IsEffectivelyVisible, Is.True);
            Assert.That(template.IsEffectivelyVisible, Is.False);
            Assert.That(target.IsEffectivelyVisible, Is.False);
            Assert.That(transcribeDescription.IsEffectivelyVisible, Is.True);
            Assert.That(editDescription.IsEffectivelyVisible, Is.False);
            Assert.That(translateDescription.IsEffectivelyVisible, Is.False);
            Assert.That(stop.IsEffectivelyVisible, Is.False);
        }

        viewModel.HistoryOverwriteMessage.Value = "History notice";
        viewModel.HasPendingHistoryResult.Value = true;
        viewModel.PartialResultMessage.Value = "Partial notice";
        viewModel.HasPartialResult.Value = true;
        viewModel.Error.Value = "Global error";
        HeadlessTestHelpers.Render();
        TextBlock historyNotice = view.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "History notice");
        TextBlock partialNotice = view.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Partial notice");
        TextBlock errorNotice = view.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Global error");
        for (int page = 0; page < 3; page++)
        {
            workflow.SelectedIndex = page;
            HeadlessTestHelpers.Render();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(historyNotice.IsEffectivelyVisible, Is.True);
                Assert.That(partialNotice.IsEffectivelyVisible, Is.True);
                Assert.That(errorNotice.IsEffectivelyVisible, Is.True);
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.SelectedSubtitlePageIndex.Value, Is.EqualTo(2));
            Assert.That(source.IsEffectivelyVisible, Is.False);
            Assert.That(template.IsEffectivelyVisible, Is.False);
            Assert.That(target.IsEffectivelyVisible, Is.True);
            Assert.That(translateDescription.IsEffectivelyVisible, Is.True);
        }

        viewModel.IsTranslating.Value = true;
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 2, Text = "Ready to edit" },
        ];
        HeadlessTestHelpers.Render();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.SelectedSubtitlePageIndex.Value, Is.EqualTo(1));
            Assert.That(workflow.SelectedIndex, Is.EqualTo(1));
            Assert.That(workflow.IsEffectivelyEnabled, Is.False,
                "Navigation stays on the page that owns the operation context.");
            Assert.That(stop.IsEffectivelyVisible, Is.True,
                "The shared cancel action remains reachable independently of page content.");
            Assert.That(template.IsEffectivelyVisible, Is.True);
            Assert.That(target.IsEffectivelyVisible, Is.False);
        }
        viewModel.IsTranslating.Value = false;
        HeadlessTestHelpers.Render();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.SelectedSubtitlePageIndex.Value, Is.EqualTo(1));
            Assert.That(workflow.SelectedIndex, Is.EqualTo(1));
            Assert.That(source.IsEffectivelyVisible, Is.False);
            Assert.That(template.IsEffectivelyVisible, Is.True);
            Assert.That(cues.IsEffectivelyVisible, Is.True);
            Assert.That(target.IsEffectivelyVisible, Is.False);
            Assert.That(editDescription.IsEffectivelyVisible, Is.True);
            Assert.That(stop.IsEffectivelyVisible, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_ReadsTheSourceAndLanguageAtADockedWidth()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        var view = new AiSubtitleView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 300, 900);

        ComboBox source = FindByAutomationName<ComboBox>(view, Strings.AiSubtitle_AudioSource);
        ComboBox language = FindByAutomationName<ComboBox>(view, Strings.AiSubtitle_SourceLanguage);

        Point sourceOrigin = source.TranslatePoint(default, view) ?? default;
        Point languageOrigin = language.TranslatePoint(default, view) ?? default;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                sourceOrigin.Y + source.Bounds.Height,
                Is.LessThanOrEqualTo(languageOrigin.Y + 1),
                "Side by side, neither reads at a docked width.");
            Assert.That(source.Bounds.Width, Is.EqualTo(language.Bounds.Width));
            Assert.That(source.Bounds.Width, Is.GreaterThan(200),
                "The source names the range it covers, so it needs the room to say so.");
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_ShowsACueAsOneRowAndOpensTheOneThatIsSelected()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 2, Text = "first line" },
            new AiTranscriptionSegment { Start = 2, End = 4, Text = "second line" },
            new AiTranscriptionSegment { Start = 4, End = 6, Text = "third line" },
        ];
        var view = new AiSubtitleView { DataContext = viewModel };
        using var shown = ShowInWindow(view, 380, 900);

        ListBox list = view.FindControl<ListBox>("CaptionCueList")!;
        ListBoxItem[] rows = list.GetRealizedContainers().OfType<ListBoxItem>().ToArray();
        Assert.That(rows, Has.Length.EqualTo(3));

        ListBoxItem opened = rows.Single(row => row.IsSelected);
        ListBoxItem[] closed = rows.Where(row => !row.IsSelected).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(closed.Select(row => row.Bounds.Height), Is.All.LessThan(72),
                "A cue nobody is editing is one row, so a whole caption set can be read.");
            Assert.That(opened.Bounds.Height, Is.GreaterThan(closed[0].Bounds.Height),
                "The one being worked on is the editor.");
            Assert.That(
                closed.SelectMany(row => row.GetVisualDescendants().OfType<TextBox>()),
                Is.Empty,
                "The fields exist only where they are being used.");
            Assert.That(
                opened.GetVisualDescendants().OfType<TextBox>().Count(),
                Is.EqualTo(5));
        }

        list.SelectedItem = closed[0].DataContext;
        HeadlessTestHelpers.Render();

        Assert.That(
            closed[0].GetVisualDescendants().OfType<TextBox>().Count(),
            Is.EqualTo(5),
            "Selecting another cue moves the editor to it.");
    }

    private static TextBlock? FindValidationText(Visual view)
        => view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => text.Text == Strings.AiPromptRequired);

    private static T FindByAutomationName<T>(Visual view, string name)
        where T : Control
        => view.GetLogicalDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetName(control) == name);
}
