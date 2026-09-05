using System.Globalization;
using System.Reactive.Linq;
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
using Beutl.Editor.Services.Captions;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

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
        var window = new Window { Content = view, Width = 340, Height = 400 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.That(view.Bounds.Height, Is.Zero,
                "Without an entitlement snapshot there is no usage to report.");

            entitlements.Value = CreateEntitlements(used: 125, limit: 500, additionalCredits: 40);
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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_GivesTheAspectRatioAndModelARowEach()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 480, Height = 640 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ImageEdit_SwitchesTasksThroughADropDown()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageEditDialogViewModel viewModel = CreateImageEditDialog(clients);
        var view = new AiImageEditView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 480, Height = 640 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_StacksTheSelectorsFromCoarsestToFinest()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 900 };

        try
        {
            window.Show();
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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_EmptyCueListDoesNotReserveAScreenful()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        using AiSubtitleDialogViewModel viewModel = CreateSubtitleDialog(clients);
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ListBox cues = view.FindControl<ListBox>("CaptionCueList")!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.Cues, Is.Empty);
                Assert.That(cues.IsVisible, Is.False,
                    "An empty cue list is a sentence, not an empty box.");
                Assert.That(
                    view.GetLogicalDescendants().OfType<TextBlock>()
                        .Any(text => text.Text == Strings.AiSubtitle_CueListEmpty
                            && text.IsVisible),
                    Is.True,
                    "It says what would put cues there.");
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_LeadsWithTheTemplatesAndKeepsTheDetailsBelowTheOptions()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 1200 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_KeepsTheDetailsAndFrameGuidanceBelowTheOptions()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 1400 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Expander templates = FindExpander(view, Strings.AiPromptTemplates);
            Expander details = FindExpander(view, Strings.AiPromptDetails);
            Expander frames = FindExpander(view, Strings.AiVideoFrameGuidanceHeader);
            TextBox prompt = view.GetLogicalDescendants().OfType<TextBox>().First(box => box.AcceptsReturn);
            CheckBox generateAudio = view.GetLogicalDescendants().OfType<CheckBox>().Single();
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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task PromptDetails_LabelEveryFieldItAsksFor()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 900 };

        try
        {
            window.Show();
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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task AiTabs_OfferThePlanOnlyWhereThereIsNoPlanYet()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_SetsTheLengthWithASliderThatStopsOnWhatTheModelTakes()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_SeedFieldCarriesTheValueBothWays()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 1200 };

        try
        {
            window.Show();
            FindExpander(view, Strings.AiPromptDetails).IsExpanded = true;
            HeadlessTestHelpers.Render();

            AssertSeedFieldCarriesTheValueBothWays(view, viewModel.Seed);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task VideoGeneration_SeedFieldCarriesTheValueBothWays()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiVideoGenerationDialogViewModel viewModel = CreateVideoGenerationDialog(clients);
        var view = new AiVideoGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 1400 };

        try
        {
            window.Show();
            FindExpander(view, Strings.AiPromptDetails).IsExpanded = true;
            HeadlessTestHelpers.Render();

            AssertSeedFieldCarriesTheValueBothWays(view, viewModel.Seed);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
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

    [AvaloniaTest]
    public async Task ImageGeneration_SaysNothingIsWrongUntilSomethingHasBeenTyped()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
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
        var window = new Window { Content = view, Width = 300, Height = 560 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task ImageGeneration_OffersAWayOutWhileTheRequestRuns()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using AiImageGenerationDialogViewModel viewModel = CreateImageGenerationDialog(clients);
        var view = new AiImageGenerationView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 420, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_ShowsProgressAndOffersOneStopForTranscriptionAndTranslation()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        using AiSubtitleDialogViewModel viewModel = CreateSubtitleDialog(clients);
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 340, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ProgressRing progress = view.FindControl<ProgressRing>("TranscriptionProgressRing")!;
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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Subtitle_ReadsTheSourceAndLanguageAtADockedWidth()
    {
        await TestReset.ResetShellAsync();
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        using AiSubtitleDialogViewModel viewModel = CreateSubtitleDialog(clients);
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 300, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
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
        var window = new Window { Content = view, Width = 380, Height = 900 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

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
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
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

    private static AiImageGenerationDialogViewModel CreateImageGenerationDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editViewModel: null,
            requestRecoveryContext: AiRetryTestContext.CreateForm());

    private static AiImageEditDialogViewModel CreateImageEditDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiImageEditingService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editViewModel: null,
            requestRecoveryContext: AiRetryTestContext.CreateForm());

    private static AiVideoGenerationDialogViewModel CreateVideoGenerationDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>(),
            clients.GetResource<IAiJobMonitor>(),
            editViewModel: null,
            requestRecoveryContext: AiRetryTestContext.CreateForm());

    private static AiSubtitleDialogViewModel CreateSubtitleDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            CaptionCatalog.CreateDefault("Default"),
            CaptionDraftStoreProvider.Current,
            Observable.Return<CaptionDraftScope?>(null));

    private static AiEntitlements CreateEntitlements(int used, int limit, int additionalCredits)
    {
        int usedPercent = limit <= 0 ? 0 : Math.Clamp((int)Math.Round(used * 100.0 / limit), 0, 100);
        return new AiEntitlements(
            "pro",
            "active",
            null,
            null,
            false,
            true,
            new AiBalance(
                new AiMonthlyUsage(usedPercent, 100 - usedPercent, limit > 0 && used >= limit),
                additionalCredits,
                false),
            new AiOperationAvailability([]));
    }
}
