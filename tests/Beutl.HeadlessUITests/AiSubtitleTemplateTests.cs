using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;
using SkiaSharp;
using AvaloniaComboBox = Avalonia.Controls.ComboBox;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaListBox = Avalonia.Controls.ListBox;
using AvaloniaTextBlock = Avalonia.Controls.TextBlock;
using AvaloniaTextBox = Avalonia.Controls.TextBox;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class AiSubtitleTemplateTests
{
    private const int SavedTemplatePackageId = -42_101;
    private const int CustomTemplatePackageId = -42_102;
    private const int PreviewTemplatePackageId = -42_103;

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

    private static TranslateTransform GetTranslate(TextBlock textBlock)
    {
        return textBlock.Transform.CurrentValue switch
        {
            TranslateTransform translate => translate,
            TransformGroup group => group.Children.OfType<TranslateTransform>().Single(),
            _ => throw new AssertionException("The subtitle does not contain a translation transform."),
        };
    }

    [Test]
    public async Task CreateCaptionTemplates_UsesExplicitRegistryOrderAndTextTemplatesOnly()
    {
        ObjectTemplateItem zulu = ObjectTemplateItem.CreateFromInstance(new TextBlock(), "Zulu");
        ObjectTemplateItem shape = ObjectTemplateItem.CreateFromInstance(new EllipseShape(), "Shape");
        ObjectTemplateItem alpha = ObjectTemplateItem.CreateFromInstance(new TextBlock(), "Alpha");

        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [zulu, shape, alpha],
            new ExtensionProvider());
        IReadOnlyList<CaptionTemplateDescriptor> result = catalog.Templates.Templates;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0].Id, Is.EqualTo(CaptionTemplateIds.DefaultText));
            Assert.That(result.Skip(1).Select(template => template.Name),
                Is.EqualTo(new[] { "Alpha", "Zulu" }));
        }
    }

    [Test]
    public void MapSegmentsToScene_AppliesElementStartTrimAndSpeed()
    {
        var source = new AudioSourceItem(
            "Audio",
            "/tmp/audio.wav",
            TimeSpan.FromSeconds(20),
            elementStart: TimeSpan.FromSeconds(10),
            elementLength: TimeSpan.FromSeconds(4),
            sourceOffset: TimeSpan.FromSeconds(2),
            speed: 200);

        AiTranscriptionSegment[] result = source.MapSegmentsToScene(
        [
            new AiTranscriptionSegment { Start = 0, End = 1, Text = "Before trim" },
            new AiTranscriptionSegment { Start = 1, End = 3, Text = "Crosses trim" },
            new AiTranscriptionSegment { Start = 4, End = 8, Text = "Middle" },
            new AiTranscriptionSegment { Start = 9, End = 12, Text = "Crosses end" },
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(segment => segment.Text),
                Is.EqualTo(new[] { "Crosses trim", "Middle", "Crosses end" }));
            Assert.That(result.Select(segment => segment.Start),
                Is.EqualTo(new[] { 10, 11, 13.5 }).Within(0.001));
            Assert.That(result.Select(segment => segment.End),
                Is.EqualTo(new[] { 10.5, 13, 14 }).Within(0.001));
        }
    }

    [AvaloniaTest]
    public async Task View_RendersCaptionTemplateDescriptorNameAndProvider()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        viewModel.SelectedSubtitlePageIndex.Value = 1;
        var view = new AiSubtitleView { DataContext = viewModel };
        var viewWindow = new AvaloniaWindow { Content = view, Width = 460, Height = 640 };
        AvaloniaWindow? itemWindow = null;

        try
        {
            viewWindow.Show();
            HeadlessTestHelpers.Render();

            AvaloniaComboBox templatePicker = view.GetVisualDescendants()
                .OfType<AvaloniaComboBox>()
                .Single(comboBox => comboBox.Name == "CaptionTemplateComboBox");
            CaptionTemplateDescriptor descriptor = viewModel.CaptionTemplates[0];
            IDataTemplate template = templatePicker.ItemTemplate!;
            AvaloniaControl content = new ContentPresenter
            {
                Content = descriptor,
                ContentTemplate = template,
            };
            itemWindow = new AvaloniaWindow { Content = content, Width = 360, Height = 120 };
            itemWindow.Show();
            HeadlessTestHelpers.Render();

            string?[] renderedText = content.GetVisualDescendants()
                .OfType<AvaloniaTextBlock>()
                .Select(textBlock => textBlock.Text)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(renderedText, Does.Contain(descriptor.Name));
                Assert.That(renderedText, Does.Contain(descriptor.ProviderId.Value));
            });
        }
        finally
        {
            itemWindow?.Close();
            viewWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task View_RendersTheSelectedCaptionTemplateOutput()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("ai-subtitle-template-preview");
        var previewFactory = new PreviewCaptionFactory();
        var template = new CaptionTemplateContribution(
            new CaptionTemplateId("beutl.tests.preview"),
            new CaptionTemplateProviderId("beutl.tests"),
            "Preview",
            previewFactory,
            DefaultCaptionPlacementPolicy.Instance);
        AiSubtitleDialogViewModel? ownedViewModel = null;
        AvaloniaWindow? ownedWindow = null;

        try
        {
            TestShell.Extensions.AddExtensions(
                PreviewTemplatePackageId,
                [new TestTemplateExtension([new CaptionTemplateRegistration(template)])]);
            AiSubtitleDialogViewModel viewModel =
                TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editor);
            ownedViewModel = viewModel;
            viewModel.SelectedCaptionTemplate.Value = viewModel.CaptionTemplates
                .Single(item => item.Id == template.Id);
            viewModel.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 2, Text = "Rendered cue" },
            ];
            await Task.Delay(200);
            HeadlessTestHelpers.Settle();
            Assert.That(previewFactory.CreateCount, Is.Zero,
                "A hidden subtitle tool must not start renderer work that delays its teardown.");
            var view = new AiSubtitleView { DataContext = viewModel };
            var window = new AvaloniaWindow { Content = view, Width = 460, Height = 640 };
            ownedWindow = window;
            window.Show();
            HeadlessTestHelpers.Render();
            Beutl.Controls.BitmapView bitmapView = view.GetVisualDescendants()
                .OfType<Beutl.Controls.BitmapView>()
                .Single(item => item.Name == "CaptionTemplatePreviewBitmap");
            AvaloniaTextBlock fallback = view.GetVisualDescendants()
                .OfType<AvaloniaTextBlock>()
                .Single(item => item.Name == "CaptionTemplatePreviewFallback");
            AutomationPeer previewPeer = ControlAutomationPeer.CreatePeerForElement(bitmapView);
            for (int attempt = 0;
                 attempt < 30 && bitmapView.Source?.Value is null;
                 attempt++)
            {
                await Task.Delay(100);
                HeadlessTestHelpers.Settle();
            }

            Assert.That(bitmapView.Source?.Value, Is.Not.Null,
                "The preview must contain the renderer output for the selected caption template.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    Math.Max(bitmapView.Source!.Value.Width, bitmapView.Source.Value.Height),
                    Is.GreaterThan(ObjectTemplatePreviewRenderer.PreviewWidth),
                    "Subtitle previews must retain more detail than saved object-template thumbnails.");
                Assert.That(bitmapView.IsEffectivelyVisible, Is.True);
                Assert.That(fallback.IsEffectivelyVisible, Is.False);
                Assert.That(previewPeer.GetAutomationControlType(), Is.EqualTo(AutomationControlType.Image));
                Assert.That(previewPeer.GetName(),
                    Is.EqualTo(Beutl.Language.Strings.AiSubtitle_TemplatePreview));
                Assert.That(previewPeer.IsContentElement(), Is.True);
                Assert.That(previewFactory.CreateCount, Is.GreaterThan(0));
            }
            (bool hasRed, bool hasGreen, bool hasBlue) = GetPreviewColors(bitmapView.Source!.Value);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hasRed, Is.True,
                    "The selected template's rendered color was not present in the preview.");
                Assert.That(hasGreen, Is.False);
                Assert.That(hasBlue, Is.True,
                    "The second element from the selected template was not rendered in the preview.");
            }

            Beutl.Media.Bitmap pagePreview = bitmapView.Source.Value;
            int rendersBeforePageChange = previewFactory.CreateCount;
            viewModel.SelectedSubtitlePageIndex.Value = 0;
            HeadlessTestHelpers.Render();
            await Task.Delay(200);
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.TemplatePreviewImage.Value, Is.Null);
                Assert.That(bitmapView.Source, Is.Null);
                Assert.That(previewFactory.CreateCount, Is.EqualTo(rendersBeforePageChange),
                    "A hidden Edit page must not keep rendering template previews.");
            }

            viewModel.BeforeTemplatePreviewAdmission = () =>
                viewModel.SelectedSubtitlePageIndex.Value = 0;
            try
            {
                viewModel.SelectedSubtitlePageIndex.Value = 1;
            }
            finally
            {
                viewModel.BeforeTemplatePreviewAdmission = null;
            }
            await Task.Delay(200);
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.SelectedSubtitlePageIndex.Value, Is.Zero);
                Assert.That(viewModel.TemplatePreviewImage.Value, Is.Null);
                Assert.That(previewFactory.CreateCount, Is.EqualTo(rendersBeforePageChange),
                    "A page switch that wins preview admission must prevent hidden renderer work.");
            }

            viewModel.SelectedSubtitlePageIndex.Value = 1;
            for (int attempt = 0;
                 attempt < 30
                 && (bitmapView.Source?.Value is null
                     || ReferenceEquals(bitmapView.Source.Value, pagePreview));
                 attempt++)
            {
                await Task.Delay(100);
                HeadlessTestHelpers.Settle();
            }
            Assert.That(bitmapView.Source?.Value, Is.Not.Null);
            Assert.That(bitmapView.Source!.Value, Is.Not.SameAs(pagePreview));

            Beutl.Media.Bitmap initialPreview = bitmapView.Source.Value;
            editor.Scene.FrameSize = new Beutl.Media.PixelSize(640, 800);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(bitmapView.Source, Is.Null);
                Assert.That(bitmapView.IsEffectivelyVisible, Is.False);
                Assert.That(fallback.IsEffectivelyVisible, Is.True,
                    "The text fallback must remain visible while a replacement preview is rendered.");
            }
            for (int attempt = 0;
                 attempt < 30
                 && (bitmapView.Source?.Value is null
                     || ReferenceEquals(bitmapView.Source.Value, initialPreview));
                 attempt++)
            {
                await Task.Delay(100);
                HeadlessTestHelpers.Settle();
            }

            Assert.That(bitmapView.Source?.Value, Is.Not.Null);
            Assert.That(bitmapView.Source!.Value, Is.Not.SameAs(initialPreview),
                "Changing the scene frame must invalidate the caption template preview.");
            (hasRed, hasGreen, hasBlue) = GetPreviewColors(bitmapView.Source.Value);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hasRed, Is.False);
                Assert.That(hasGreen, Is.True,
                    "The preview did not use the updated frame-height placement context.");
                Assert.That(hasBlue, Is.True);
                Assert.That(bitmapView.IsEffectivelyVisible, Is.True);
                Assert.That(fallback.IsEffectivelyVisible, Is.False);
            }

            int rendersBeforeUnload = previewFactory.CreateCount;
            viewModel.BeforeTemplatePreviewAdmission = window.Close;
            try
            {
                editor.Scene.FrameSize = new Beutl.Media.PixelSize(640, 900);
            }
            finally
            {
                viewModel.BeforeTemplatePreviewAdmission = null;
            }
            await Task.Delay(200);
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(window.IsVisible, Is.False);
                Assert.That(viewModel.TemplatePreviewImage.Value, Is.Null);
                Assert.That(previewFactory.CreateCount, Is.EqualTo(rendersBeforeUnload),
                    "The last unload must close preview admission before a concurrent refresh installs work.");
            }
        }
        finally
        {
            try
            {
                ownedWindow?.Close();
                HeadlessTestHelpers.Settle();
            }
            finally
            {
                try
                {
                    if (ownedViewModel is not null)
                        await ownedViewModel.DisposeAsync();
                }
                finally
                {
                    TestShell.Extensions.RemoveExtensions(PreviewTemplatePackageId);
                }
            }
        }
    }

    private static (bool Red, bool Green, bool Blue) GetPreviewColors(Beutl.Media.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);
        using SKBitmap decoded = SKBitmap.Decode(stream.ToArray());
        bool hasRed = false;
        bool hasGreen = false;
        bool hasBlue = false;
        for (int y = 0; y < decoded.Height && (!hasRed || !hasGreen || !hasBlue); y++)
        {
            for (int x = 0; x < decoded.Width; x++)
            {
                SKColor pixel = decoded.GetPixel(x, y);
                hasRed |= pixel.Alpha > 80 && pixel.Red > 160 && pixel.Green < 120;
                hasGreen |= pixel.Alpha > 80 && pixel.Green > 160 && pixel.Red < 120;
                hasBlue |= pixel.Alpha > 80 && pixel.Blue > 160 && pixel.Red < 120;
            }
        }
        return (hasRed, hasGreen, hasBlue);
    }

    private sealed class PreviewCaptionFactory : ICaptionElementFactory
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
        {
            Interlocked.Increment(ref _createCount);
            Color primaryColor = cue.Text != "Rendered cue"
                ? Colors.Yellow
                : context.DefaultPosition.Y < 200
                    ? Colors.Red
                    : Colors.Lime;
            return
            [
                context.CreateDescription(
                    cue,
                    () => new TextBlock
                    {
                        Text = { CurrentValue = cue.Text },
                        Size = { CurrentValue = 96 },
                        Fill = { CurrentValue = new SolidColorBrush(primaryColor) },
                    },
                    position: new Beutl.Graphics.Point(-140, context.DefaultPosition.Y)),
                context.CreateDescription(
                    cue,
                    () => new RectShape
                    {
                        Width = { CurrentValue = 80 },
                        Height = { CurrentValue = 60 },
                        Fill = { CurrentValue = new SolidColorBrush(Colors.Blue) },
                    },
                    position: new Beutl.Graphics.Point(140, context.DefaultPosition.Y)),
            ];
        }
    }

    [AvaloniaTest]
    public async Task TemplatePreview_PageChangeCancelsAnAdmittedRender()
    {
        await TestReset.ResetShellAsync();
        var renderStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRenderer = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] latePng;
        using (var bitmap = new Beutl.Media.Bitmap(2, 2))
        using (var stream = new MemoryStream())
        {
            Assert.That(bitmap.Save(stream, EncodedImageFormat.Png), Is.True);
            latePng = stream.ToArray();
        }
        AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        viewModel.SelectedSubtitlePageIndex.Value = 1;
        viewModel.TemplatePreviewRenderer = async (_, _, cancellationToken) =>
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => cancellationObserved.TrySetResult());
            renderStarted.TrySetResult();
            await releaseRenderer.Task;
            return latePng;
        };
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new AvaloniaWindow { Content = view, Width = 320, Height = 640 };
        Task? disposal = null;

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            await renderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            viewModel.SelectedSubtitlePageIndex.Value = 0;
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Beutl.Controls.BitmapView preview = view.GetVisualDescendants()
                .OfType<Beutl.Controls.BitmapView>()
                .Single(item => item.Name == "CaptionTemplatePreviewBitmap");
            window.Close();
            HeadlessTestHelpers.Settle();
            disposal = viewModel.DisposeAsync().AsTask();
            await Task.Delay(100);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(disposal.IsCompleted, Is.False,
                    "Disposal must drain the admitted renderer even after its page is hidden.");
                Assert.That(viewModel.TemplatePreviewImage.Value, Is.Null);
                Assert.That(preview.Source, Is.Null);
                Assert.That(viewModel.SelectedSubtitlePageIndex.Value, Is.Zero);
            }

            releaseRenderer.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();
            Assert.That(preview.Source, Is.Null,
                "Bytes returned after cancellation must not publish a late preview image.");
        }
        finally
        {
            releaseRenderer.TrySetResult();
            if (window.IsVisible)
                window.Close();
            HeadlessTestHelpers.Settle();
            disposal ??= viewModel.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public async Task CueEditor_NarrowWidthKeepsFieldsUsableAndBindsCaretForSplit()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 4, Text = "hello world" },
        ];
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new AvaloniaWindow { Content = view, Width = 280, Height = 640 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            AvaloniaTextBox[] cueFields = view.GetVisualDescendants()
                .OfType<AvaloniaTextBox>()
                .Where(textBox =>
                {
                    string? name = AutomationProperties.GetName(textBox);
                    return name == Beutl.Language.Strings.AiSubtitle_CueStart
                        || name == Beutl.Language.Strings.AiSubtitle_CueEnd
                        || name == Beutl.Language.Strings.AiSubtitle_Speaker
                        || name == Beutl.Language.Strings.AiSubtitle_Language
                        || name == Beutl.Language.Strings.AiSubtitle_Text;
                })
                .ToArray();
            AvaloniaTextBox captionText = cueFields.Single(textBox =>
                AutomationProperties.GetName(textBox) == Beutl.Language.Strings.AiSubtitle_Text);
            AvaloniaListBox cueList = view.GetLogicalDescendants()
                .OfType<AvaloniaListBox>()
                .Single(listBox => listBox.Name == "CaptionCueList");
            double rightmostFieldEdge = cueFields.Max(field =>
                field.TranslatePoint(new Avalonia.Point(field.Bounds.Width, 0), cueList)?.X
                ?? double.PositiveInfinity);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cueFields, Has.Length.EqualTo(5));
                Assert.That(cueFields.All(field => field.Bounds.Width > 0), Is.True);
                Assert.That(rightmostFieldEdge,
                    Is.LessThanOrEqualTo(cueList.Bounds.Width + 1));
                Assert.That(viewModel.SplitCue.CanExecute(), Is.False);
            }

            captionText.CaretIndex = 5;
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Cues[0].CaretIndex, Is.EqualTo(5));
                Assert.That(viewModel.SplitCue.CanExecute(), Is.True);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task SelectingAnotherAudioSource_ClearsTranscriptionResult()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        var first = new AudioSourceItem("First", "/tmp/first.wav", TimeSpan.FromSeconds(10));
        var second = new AudioSourceItem("Second", "/tmp/second.wav", TimeSpan.FromSeconds(10));
        viewModel.SelectedAudioSource.Value = first;
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 1, Text = "Stale" },
        ];

        viewModel.SelectedAudioSource.Value = second;
        HeadlessTestHelpers.Settle();

        Assert.That(viewModel.ResultSegments.Value, Is.Null);
        Assert.That(viewModel.CanAddToScene.Value, Is.False);
    }

    [Test]
    public void AudioSourceResumeIdentity_RequiresEquivalentTimelineMapping()
    {
        Guid elementId = Guid.NewGuid();
        var first = new AudioSourceItem(
            "First",
            "/tmp/source.flac",
            TimeSpan.FromSeconds(30),
            elementStart: TimeSpan.FromSeconds(2),
            elementLength: TimeSpan.FromSeconds(10),
            sourceOffset: TimeSpan.FromSeconds(1),
            speed: 125,
            elementId: elementId);
        var equivalent = new AudioSourceItem(
            "Renamed",
            "/tmp/source.flac",
            TimeSpan.FromSeconds(30),
            elementStart: TimeSpan.FromSeconds(2),
            elementLength: TimeSpan.FromSeconds(10),
            sourceOffset: TimeSpan.FromSeconds(1),
            speed: 125,
            elementId: elementId);
        var changedMapping = new AudioSourceItem(
            "Changed",
            "/tmp/source.flac",
            TimeSpan.FromSeconds(30),
            elementStart: TimeSpan.FromSeconds(3),
            elementLength: TimeSpan.FromSeconds(10),
            sourceOffset: TimeSpan.FromSeconds(1),
            speed: 125,
            elementId: elementId);
        var anotherElement = new AudioSourceItem(
            "Other element",
            "/tmp/source.flac",
            TimeSpan.FromSeconds(30),
            elementStart: TimeSpan.FromSeconds(2),
            elementLength: TimeSpan.FromSeconds(10),
            sourceOffset: TimeSpan.FromSeconds(1),
            speed: 125,
            elementId: Guid.NewGuid());
        string relativePath = Path.GetRelativePath(
            Environment.CurrentDirectory,
            Path.GetFullPath("/tmp/source.flac"));
        var equivalentRelativePath = new AudioSourceItem(
            "Relative",
            relativePath,
            TimeSpan.FromSeconds(30),
            elementStart: TimeSpan.FromSeconds(2),
            elementLength: TimeSpan.FromSeconds(10),
            sourceOffset: TimeSpan.FromSeconds(1),
            speed: 125,
            elementId: elementId);

        Assert.Multiple(() =>
        {
            Assert.That(AudioSourceItem.CanResume(equivalent, first), Is.True);
            Assert.That(AudioSourceItem.CanResume(equivalentRelativePath, first), Is.True);
            Assert.That(AudioSourceItem.CanResume(changedMapping, first), Is.False);
            Assert.That(AudioSourceItem.CanResume(anotherElement, first), Is.False);
            Assert.That(AudioSourceItem.CanResume(null, first), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task LoadHistoryResult_ConfirmsBeforeReplacingUnsavedCaptionsInTheReusedTab()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 1, Text = "Work in progress" },
        ];
        HeadlessTestHelpers.Settle();

        viewModel.LoadHistoryResult(CreateHistoryResult("Imported from history"));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.HasPendingHistoryResult.Value, Is.True);
            Assert.That(
                viewModel.HistoryOverwriteMessage.Value,
                Is.EqualTo(Beutl.Language.Strings.AiSubtitle_HistoryOverwritePrompt));
            Assert.That(
                viewModel.ResultSegments.Value!.Single().Text,
                Is.EqualTo("Work in progress"),
                "The pending import must not discard unsaved captions before confirmation.");
        }

        viewModel.DiscardPendingHistoryResult();
        HeadlessTestHelpers.Settle();
        Assert.That(viewModel.ResultSegments.Value!.Single().Text, Is.EqualTo("Work in progress"));

        viewModel.LoadHistoryResult(CreateHistoryResult("Imported from history"));
        viewModel.ConfirmPendingHistoryResult();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.HasPendingHistoryResult.Value, Is.False);
            Assert.That(
                viewModel.ResultSegments.Value!.Single().Text,
                Is.EqualTo("Imported from history"));
        }
    }

    [AvaloniaTest]
    public async Task LoadHistoryResult_AppliesImmediatelyWhenTheTabHasNoUnsavedWork()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(null);

        viewModel.LoadHistoryResult(CreateHistoryResult("Imported from history"));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.HasPendingHistoryResult.Value, Is.False);
            Assert.That(
                viewModel.ResultSegments.Value!.Single().Text,
                Is.EqualTo("Imported from history"));
        }
    }

    private static AiCaptionHistoryResult CreateHistoryResult(string text)
        => new(
            new AiJobId("caption-history-job"),
            [new AiTranscriptionSegment { Start = 0, End = 1, Text = text }],
            "en");

    [AvaloniaTest]
    public async Task AddToScene_WithDefaultTemplate_CreatesPositionedIndependentSubtitles()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("ai-subtitle-default-template");
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editor);
        viewModel.ResultSegments.Value =
        [
            new AiTranscriptionSegment { Start = 0, End = 1.25, Text = "First" },
            new AiTranscriptionSegment { Start = 2, End = 2.1, Text = "Second" },
        ];
        HeadlessTestHelpers.Settle();

        await viewModel.AddToScene.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        Element[] elements = editor.Scene.Children.ToArray();
        TextBlock[] subtitles = elements.Select(element => element.Objects.OfType<TextBlock>().Single()).ToArray();
        float expectedY = editor.Scene.FrameSize.Height * 0.85f - editor.Scene.FrameSize.Height / 2f;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(elements, Has.Length.EqualTo(2));
            Assert.That(elements.Select(element => element.Start),
                Is.EqualTo(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2) }));
            Assert.That(elements.Select(element => element.Length),
                Is.EqualTo(new[] { TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(0.5) }));
            Assert.That(subtitles.Select(subtitle => subtitle.Text.CurrentValue),
                Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(subtitles.Select(subtitle => subtitle.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(subtitles.All(subtitle => subtitle.Size.CurrentValue == 48), Is.True);
            Assert.That(subtitles.All(subtitle =>
                subtitle.FontFamily.CurrentValue?.Name == CaptionPresentationDefaults.FontFamilyName), Is.True);
            Assert.That(subtitles.All(subtitle =>
            {
                TranslateTransform translate = GetTranslate(subtitle);
                return translate.X.CurrentValue == 0
                       && Math.Abs(translate.Y.CurrentValue - expectedY) < 0.001f;
            }), Is.True);
        }
    }

    [AvaloniaTest]
    public async Task AddToScene_WithSavedTemplate_PreservesTemplateStyleAndPlacement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("ai-subtitle-saved-template");
        var source = new TextBlock
        {
            Text = { CurrentValue = "Placeholder" },
            Size = { CurrentValue = 72 },
            FontWeight = { CurrentValue = FontWeight.Bold },
            Transform = { CurrentValue = new TranslateTransform(45, 60) },
        };
        ObjectTemplateItem item = ObjectTemplateItem.CreateFromInstance(source, "Saved subtitle");
        CaptionTemplateContribution contribution = TextBlockCaptionTemplateAdapter.TryCreate(item)!;
        TestShell.Extensions.AddExtensions(
            SavedTemplatePackageId,
            [new TestTemplateExtension([new CaptionTemplateRegistration(contribution)])]);
        try
        {
            using AiSubtitleDialogViewModel viewModel =
                TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editor);
            viewModel.SelectedCaptionTemplate.Value = viewModel.CaptionTemplates
                .Single(template => template.Id == contribution.Id);
            viewModel.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 1, End = 3, Text = "Generated text" },
            ];
            HeadlessTestHelpers.Settle();

            await viewModel.AddToScene.ExecuteAsync();
            HeadlessTestHelpers.Settle();
        }
        finally
        {
            TestShell.Extensions.RemoveExtensions(SavedTemplatePackageId);
        }

        Element element = editor.Scene.Children.Single();
        TextBlock subtitle = element.Objects.OfType<TextBlock>().Single();
        TranslateTransform translate = GetTranslate(subtitle);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(subtitle.Text.CurrentValue, Is.EqualTo("Generated text"));
            Assert.That(subtitle.Size.CurrentValue, Is.EqualTo(72));
            Assert.That(subtitle.FontWeight.CurrentValue, Is.EqualTo(FontWeight.Bold));
            Assert.That(translate.X.CurrentValue, Is.EqualTo(45));
            Assert.That(translate.Y.CurrentValue, Is.EqualTo(60));
            Assert.That(subtitle.Id, Is.Not.EqualTo(source.Id));
        }
    }

    [AvaloniaTest]
    public async Task AddToScene_WithCustomFactoryAddsEveryElementForEachCue()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("ai-subtitle-multi-template");
        var template = new CaptionTemplateContribution(
            new CaptionTemplateId("beutl.tests.bilingual"),
            new CaptionTemplateProviderId("beutl.tests"),
            "Bilingual",
            new BilingualCaptionFactory(),
            DefaultCaptionPlacementPolicy.Instance);
        TestShell.Extensions.AddExtensions(
            CustomTemplatePackageId,
            [new TestTemplateExtension([new CaptionTemplateRegistration(template)])]);
        try
        {
            using AiSubtitleDialogViewModel viewModel =
                TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editor);
            viewModel.SelectedCaptionTemplate.Value = viewModel.CaptionTemplates
                .Single(descriptor => descriptor.Id == template.Id);
            viewModel.ResultSegments.Value =
            [
                new AiTranscriptionSegment { Start = 0, End = 2, Text = "Hello" },
            ];
            HeadlessTestHelpers.Settle();

            await viewModel.AddToScene.ExecuteAsync();
            HeadlessTestHelpers.Settle();
        }
        finally
        {
            TestShell.Extensions.RemoveExtensions(CustomTemplatePackageId);
        }

        Element[] elements = editor.Scene.Children.OrderBy(element => element.ZIndex).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(elements, Has.Length.EqualTo(2));
            Assert.That(elements.Select(element => element.ZIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(
                elements.Select(element => element.Objects.OfType<TextBlock>().Single().Text.CurrentValue),
                Is.EqualTo(new[] { "Hello", "Hello translation" }));
        }
    }

    private sealed class BilingualCaptionFactory : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            =>
            [
                context.CreateDescription(
                    cue,
                    () => new TextBlock { Text = { CurrentValue = cue.Text } }),
                context.CreateDescription(
                    cue,
                    () => new TextBlock { Text = { CurrentValue = cue.Text + " translation" } },
                    layerOffset: 1),
            ];
    }

    private sealed class TestTemplateExtension(
        IReadOnlyCollection<CaptionTemplateRegistration> registrations)
        : CaptionTemplateExtension
    {
        public override IReadOnlyCollection<CaptionTemplateRegistration> Registrations
            => registrations;
    }
}
