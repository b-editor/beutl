using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using AvaPoint = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class TimelineAiViewTests
{
    private static async Task<(EditViewModel Editor, TimelineTabViewModel Timeline)> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, workspace))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()
                                        ?? throw new InvalidOperationException("No timeline tab.");
        return (editor, timeline);
    }

    // Through the editor's adder, so the element has the file of its own that clicking it needs.
    private static async Task<Element> AddImage(EditViewModel editor, TimeSpan start, int layer)
    {
        string resources = Beutl.Services.AI.AiResultImporter.GetResourceDirectory(editor.Scene);
        Directory.CreateDirectory(resources);
        string path = Path.Combine(resources, $"{Guid.NewGuid():N}.png");
        using (var bitmap = new Bitmap(16, 16))
            Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        Beutl.Editor.Services.ElementAddResult result = await editor.GetRequiredService<Beutl.Editor.Services.IElementAdder>().AddAsync(
            [new Beutl.Editor.Models.ElementDescription(start, TimeSpan.FromSeconds(2), layer, new Beutl.Editor.Models.ElementSource.File(path))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        Assert.That(result.IsSuccess, Is.True, result.Failure?.Message);
        return result.Elements.Single();
    }

    private static FAMenuFlyoutSubItem OpenAiMenu(ElementView view)
    {
        var border = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "border");
        var flyout = (FAMenuFlyout)border.ContextFlyout!;
        flyout.ShowAt(border);
        HeadlessTestHelpers.Settle();
        FAMenuFlyoutSubItem menu = flyout.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Name == "aiActions");
        flyout.Hide();
        HeadlessTestHelpers.Settle();
        return menu;
    }

    [AvaloniaTest]
    public async Task APictureOffersTheImageEditsAndAVideoFromIt()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-menu-image");
        Element element = await AddImage(editor, TimeSpan.Zero, 0);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1200, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Settle();
            ElementView elementView = view.GetVisualDescendants().OfType<ElementView>()
                .Single(candidate => ((ElementViewModel)candidate.DataContext!).Model == element);

            FAMenuFlyoutSubItem menu = OpenAiMenu(elementView);

            string[] texts = menu.Items.OfType<FAMenuFlyoutItem>().Select(item => item.Text ?? string.Empty).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(menu.IsVisible, Is.True);
                Assert.That(texts, Does.Contain(Strings.AiEditRemoveBackground));
                Assert.That(texts, Does.Contain(Strings.AiTimelineVideoFromImage));
                Assert.That(texts, Does.Not.Contain(Strings.AiTimelineRegenerate), "nothing generated it");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task AShapeOffersNoAiActions()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-menu-shape");
        await editor.GetRequiredService<Beutl.Editor.Services.IElementAdder>().AddAsync(
            [new Beutl.Editor.Models.ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, new Beutl.Editor.Models.ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1200, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Settle();
            ElementView elementView = view.GetVisualDescendants().OfType<ElementView>().Single();

            Assert.That(OpenAiMenu(elementView).IsVisible, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ChoosingAnEditShowsAPlaceholderOnTheClipUntilThePopupCloses()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-placeholder");
        Element element = await AddImage(editor, TimeSpan.FromSeconds(1), 1);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1200, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Settle();
            var requested = new List<TimelineGenerationJob>();
            timeline.GenerationPopupRequested += (_, job) => requested.Add(job);

            await timeline.StartAiActionAsync(element, TimelineAiAction.RemoveBackground);
            HeadlessTestHelpers.Settle();

            TimelineGenerationJob job = timeline.GenerationService!.Jobs.Single();
            GenerationPlaceholderView placeholder = view.GetVisualDescendants().OfType<GenerationPlaceholderView>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(requested, Is.EqualTo(new[] { job }));
                Assert.That(job.Slot.Value, Is.EqualTo(new TimelineGenerationSlot(1, element.Range)));
                Assert.That(job.Inputs.Value.ImagePath, Is.Not.Null);
                Assert.That(placeholder.IsVisible, Is.True);
            });

            timeline.GenerationService.Remove(job);
            HeadlessTestHelpers.Settle();
            Assert.That(view.GetVisualDescendants().OfType<GenerationPlaceholderView>(), Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ADoubleClickOnAnEmptyStretchStartsAClipForItAndOneOnAClipDoesNot()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-double-click");
        await AddImage(editor, TimeSpan.Zero, 0);
        await AddImage(editor, TimeSpan.FromSeconds(6), 0);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1600, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Settle();
            ElementView first = view.GetVisualDescendants().OfType<ElementView>()
                .Single(candidate => ((ElementViewModel)candidate.DataContext!).Model.Start == TimeSpan.Zero);
            Border clip = first.GetVisualDescendants().OfType<Border>().First(b => b.Name == "border");
            double middle = clip.Bounds.Height / 2;
            AvaPoint onClip = clip.TranslatePoint(new AvaPoint(clip.Bounds.Width / 2, middle), window)!.Value;
            // Half a clip's width past its end: inside the gap before the next one.
            AvaPoint inGap = clip.TranslatePoint(new AvaPoint(clip.Bounds.Width * 1.5, middle), window)!.Value;

            DoubleClick(window, onClip);
            Assert.That(timeline.GenerationService!.Jobs, Is.Empty, "a double-click on a clip is the clip's");

            DoubleClick(window, inGap);
            TimelineGenerationJob job = timeline.GenerationService.Jobs.Single();
            Assert.That(job.Target.Gap?.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(job.Target.Gap?.MaxLength, Is.EqualTo(TimeSpan.FromSeconds(4)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ThePopupFitsTheSceneAndTheGapAndWritesToTheJob()
    {
        await TestReset.ResetShellAsync();
        (_, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-popup-shape");
        TimelineGenerationService service = timeline.GenerationService!;
        TimelineGenerationJob job = service.Create(
            TimelineGenerationTarget.ForGap(new TimelineGap(0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), null)),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video },
            new TimelineGenerationInputs());
        var catalog = new FakeCatalog(new GenerativeModelInfo(
            "video-model",
            "Video model",
            IsDefault: true,
            IsAvailable: true,
            Image: null,
            Video: GenerativeVideoCapabilities.Unrestricted with
            {
                DurationsSeconds = [4, 6, 8],
                Resolutions = ["720p", "1080p"],
                AspectRatios = ["16:9", "9:16"],
            }));

        using var popup = new TimelineAiPopupViewModel(job, service, catalog, host: null, new Beutl.Media.PixelSize(1080, 1920), 30);
        await WaitUntilAsync(() => !popup.IsLoadingModels.Value);

        Assert.Multiple(() =>
        {
            Assert.That(popup.SelectedDuration.Value, Is.EqualTo(4), "the shortest clip that fills the 3 s gap");
            Assert.That(popup.SelectedAspectRatio.Value, Is.EqualTo("9:16"), "a portrait scene");
            Assert.That(popup.SelectedResolution.Value, Is.EqualTo("1080p"));
            Assert.That(popup.Generate.CanExecute(), Is.False, "a clip needs a prompt");
        });

        popup.Prompt.Value = "waves at dusk";
        await WaitUntilAsync(() => job.Spec.Value.Prompt == "waves at dusk");
        Assert.Multiple(() =>
        {
            Assert.That(job.Spec.Value.AspectRatio, Is.EqualTo("9:16"));
            Assert.That(job.Spec.Value.ModelId, Is.Null, "the default model is the service's choice");
            Assert.That(popup.Generate.CanExecute(), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task ClosingThePopupWhileAFileDialogIsOpenIsHarmless()
    {
        await TestReset.ResetShellAsync();
        (_, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-popup-picker");
        TimelineGenerationService service = timeline.GenerationService!;
        TimelineGenerationJob job = service.Create(
            TimelineGenerationTarget.ForGap(new TimelineGap(0, TimeSpan.Zero, null, null)),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video, Prompt = "rain" },
            new TimelineGenerationInputs());
        var dialog = new TaskCompletionSource<string?>();
        var popup = new TimelineAiPopupViewModel(job, service, new FakeCatalog(), null, null, 30)
        {
            PickImageFile = token =>
            {
                // The dialog answers whatever the popup did meanwhile.
                token.Register(() => dialog.TrySetResult("/tmp/picked.png"));
                return dialog.Task;
            },
        };

        Task choosing = popup.ChooseLastFrameAsync();
        popup.Dispose();
        await choosing;

        Assert.That(job.Inputs.Value.LastFramePath, Is.Null, "a closed popup takes nothing from the dialog");
        service.Remove(job);
    }

    [AvaloniaTest]
    public async Task ThePopupStaysOpenUntilItsCloseButtonIsPressed()
    {
        await TestReset.ResetShellAsync();
        (_, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-popup-close");
        TimelineGenerationService service = timeline.GenerationService!;
        TimelineGenerationJob job = service.Create(
            TimelineGenerationTarget.ForGap(new TimelineGap(0, TimeSpan.Zero, null, null)),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video, Prompt = "rain" },
            new TimelineGenerationInputs());
        using var popup = new TimelineAiPopupViewModel(job, service, new FakeCatalog(), null, null, 30);
        var anchor = new Button { Content = "anchor", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var window = new Window { Content = anchor, Width = 900, Height = 600 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Settle();
            var flyout = new TimelineAiPopupFlyout(popup);
            flyout.ShowAt(anchor);
            HeadlessTestHelpers.Settle();

            window.MouseDown(new AvaPoint(5, 5), MouseButton.Left);
            window.MouseUp(new AvaPoint(5, 5), MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Assert.That(flyout.IsOpen, Is.True, "a click elsewhere, or a file dialog taking focus, leaves it open");

            Button close = flyout.Presenter!.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "CloseButton");
            Assert.That(close.IsVisible, Is.True);
            close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();
            Assert.That(flyout.IsOpen, Is.False);
        }
        finally
        {
            window.Close();
            service.Remove(job);
        }
    }

    [AvaloniaTest]
    public async Task ASignedOutAccountSeesTheGateInsteadOfGenerate()
    {
        await TestReset.ResetShellAsync();
        (_, TimelineTabViewModel timeline) = await OpenEditor("timeline-ai-popup-gate");
        TimelineGenerationService service = timeline.GenerationService!;
        TimelineGenerationJob job = service.Create(
            TimelineGenerationTarget.ForGap(new TimelineGap(0, TimeSpan.Zero, null, null)),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video, Prompt = "rain" },
            new TimelineGenerationInputs());

        using var popup = new TimelineAiPopupViewModel(
            job, service, new FakeCatalog(), new FakeHost(TimelineAiAccess.SignInRequired), null, 30);
        // The account's state reaches the popup on the UI thread, after the models.
        await WaitUntilAsync(() => !popup.IsLoadingModels.Value && popup.Access.Value != TimelineAiAccess.Loading);

        Assert.Multiple(() =>
        {
            Assert.That(popup.IsGated.Value, Is.True);
            Assert.That(popup.GateMessage.Value, Is.EqualTo(Strings.AiTimelineSignInRequired));
            Assert.That(popup.Generate.CanExecute(), Is.False);
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True);
    }

    private sealed class FakeCatalog(params GenerativeModelInfo[] models) : IGenerativeModelCatalog
    {
        public Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(string operationId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GenerativeModelInfo>>(models);
    }

    private sealed class FakeHost(TimelineAiAccess access) : ITimelineAiHost
    {
        public IObservable<TimelineAiAccess> Access => System.Reactive.Linq.Observable.Return(access);

        public void OpenAiWorkspace()
        {
        }

        public void OpenSubtitles(Element element)
        {
        }

        public ITimelineAiUsageEstimate CreateUsageEstimate() => new Estimate();

        public string GetResourceDirectory(Scene scene) => Path.GetTempPath();

        private sealed class Estimate : ITimelineAiUsageEstimate
        {
            public IObservable<bool> CanAfford => System.Reactive.Linq.Observable.Return(true);

            public IObservable<string> Explanation => System.Reactive.Linq.Observable.Return(string.Empty);

            public void Check(string operationId, string? modelId, int? durationSeconds)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    private static void DoubleClick(Window window, AvaPoint point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Settle();
    }
}
