using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Converters;
using Beutl.Editor.Services.Captions;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;
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
    public async Task ImageGeneration_PairsTheAspectRatioWithTheModelPickerOnOneRow()
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
            Assert.That(aspectRatio.Parent, Is.SameAs(model.Parent).And.TypeOf<Grid>(),
                "The two selectors share one row instead of stacking.");

            viewModel.ModelPicker.HasChoice.Value = true;
            HeadlessTestHelpers.Render();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsVisible, Is.True);
                Assert.That(Grid.GetColumn(aspectRatio), Is.Zero);
                Assert.That(Grid.GetColumn(model), Is.EqualTo(1));
                Assert.That(Grid.GetColumnSpan(aspectRatio), Is.EqualTo(1));
                Assert.That(aspectRatio.Bounds.Right, Is.LessThanOrEqualTo(model.Bounds.Left + 1));
            }

            viewModel.ModelPicker.HasChoice.Value = false;
            HeadlessTestHelpers.Render();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(model.IsVisible, Is.False);
                Assert.That(Grid.GetColumnSpan(aspectRatio), Is.EqualTo(2),
                    "With a single model on offer the aspect ratio takes the whole row.");
                Assert.That(aspectRatio.Bounds.Width, Is.GreaterThan(((Grid)model.Parent!).Bounds.Width / 2));
            }
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
            Assert.That(viewModel.Cues, Is.Empty);
            Assert.That(cues.Bounds.Height, Is.LessThanOrEqualTo(80),
                "An empty cue list must not push the translation actions off screen.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void GridSpan_FollowsWhetherTheNeighbourIsShown(bool hasNeighbour, int expected)
    {
        Assert.That(
            GridSpanConverter.Instance.Convert(hasNeighbour, typeof(int), null, CultureInfo.InvariantCulture),
            Is.EqualTo(expected));
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
            Expander reference = FindExpander(view, Strings.AiReferenceImage);
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
            Expander frames = FindExpander(view, Strings.AiVideoFrameGuidance);
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

    [TestCase("3", 3)]
    [TestCase(4, 4)]
    [TestCase("nonsense", 2)]
    [TestCase(null, 2)]
    public void GridSpan_TakesTheRowWidthFromTheConverterParameter(object? parameter, int expected)
    {
        Assert.That(
            GridSpanConverter.Instance.Convert(false, typeof(int), parameter, CultureInfo.InvariantCulture),
            Is.EqualTo(expected));
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

    private static AiImageGenerationDialogViewModel CreateImageGenerationDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>());

    private static AiImageEditDialogViewModel CreateImageEditDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiImageEditingService>(),
            clients.GetResource<IAuthenticatedContentService>());

    private static AiVideoGenerationDialogViewModel CreateVideoGenerationDialog(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>(),
            clients.GetResource<IAiJobMonitor>());

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
