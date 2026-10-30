using System.Diagnostics;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.SilenceSplitTab;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.SilenceSplitTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SilenceSplitTabTests
{
    [AvaloniaTest]
    public async Task GroupedSceneExpression_CanBeAnalyzedAndObservesTheReferencedScene()
    {
        var (project, scene, editor) = await TestShell.OpenNewSceneAsync("silence-expression");
        var referenced = new Scene { Duration = { Value = MediaTime.FromSeconds(2) } };
        project.Items.Add(referenced);
        var sound = new SceneSound();
        sound.ReferencedScene.Expression = new ReferenceExpression<Scene?>(referenced.Id.Value);
        var element = new Element { Length = { Value = referenced.Duration.Value } };
        element.Objects.Add(new SoundGroup { Children = { new SoundGroup { Children = { sound } } } });
        scene.Children.Add(element);
        using var model = new SilenceSplitViewModel(scene, element, editor.HistoryManager);

        Assert.That(SilenceSplitViewModel.CanAnalyze(scene, element), Is.True);
        await model.AnalyzeAsync();
        Assert.That(model.CanApply.Value, Is.True, model.Status.Value);
        referenced.Children.Add(new Element { Length = { Value = MediaTime.FromSeconds(1) } });
        Assert.That(model.CanApply.Value, Is.False);
        Assert.That(model.Status.Value, Is.EqualTo(Strings.SilenceSplit_Stale));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Analysis_CapturesBeforeDecodingAndStopsWhenTheDocumentChanges(bool nested)
    {
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("silence-analysis-edit");
        string path = Path.Combine(TestShell.ProjectRoot(editor), "audio.silence-analysis");
        File.WriteAllBytes(path, []);
        var child = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        child.Objects.Add(new SourceSound { Source = { Value = SoundSource.Open(path) } });
        var referenced = new Scene { Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "nested.scene")) };
        var element = child;
        if (nested)
        {
            referenced.Children.Add(child);
            element = new Element { Length = { Value = child.Length.Value } };
            element.Objects.Add(new SceneSound { ReferencedScene = { Value = referenced } });
        }
        scene.AddChild(element);
        editor.HistoryManager.Commit("Setup");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        bool openedOnUi = false;
        var decoder = new Moq.Mock<IDecoderInfo>();
        decoder.Setup(d => d.IsSupported(path)).Returns(true);
        decoder.Setup(d => d.AudioExtensions()).Returns([".silence-analysis"]);
        decoder.Setup(d => d.VideoExtensions()).Returns([]);
        decoder.Setup(d => d.Open(path, Moq.It.IsAny<MediaOptions>())).Returns(() =>
        {
            openedOnUi |= Dispatcher.UIThread.CheckAccess();
            started.TrySetResult();
            if (!openedOnUi && !release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return null;
        });
        DecoderRegistry.Register(decoder.Object);
        using var model = new SilenceSplitViewModel(scene, element, editor.HistoryManager);
        Task analysis = model.AnalyzeAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(openedOnUi, Is.False);
            if (nested) referenced.Children.Clear(); else scene.Children.Remove(element);
            release.Set();
            await analysis;
            Assert.That(model.CanApply.Value || model.IsBusy.Value, Is.False);
            Assert.That(model.Status.Value, Is.EqualTo(Strings.SilenceSplit_Stale));
        }
        finally
        {
            release.Set();
            await analysis;
            DecoderRegistry.Unregister(decoder.Object);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedPreparation_OnlyInvalidatesRangesWhenTheDocumentChanged(bool edit)
    {
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("silence-failed-prepare");
        string path = Path.Combine(TestShell.ProjectRoot(editor), "audio.silence-retry");
        File.WriteAllBytes(path, []);
        var audio = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        audio.Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "audio.belm"));
        audio.Objects.Add(new SourceSound { Source = { Value = SoundSource.Open(path) } });
        var video = new Element { Length = { Value = MediaTime.FromSeconds(4.01) }, ZIndex = { Value = 1 } };
        video.Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "video.belm"));
        video.Objects.Add(new Beutl.Graphics.SourceVideo());
        scene.AddChild(audio);
        scene.AddChild(video);
        scene.Groups.Add([audio.Id.Value, video.Id.Value]);
        editor.HistoryManager.Commit("Setup");
        using var tab = new SilenceSplitTabViewModel(editor);
        tab.Show(audio);
        var model = tab.Analysis.Value!;
        await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
        model.PaddingMilliseconds.Value = 0;
        model.SetWaveform(Enumerable.Range(0, 500).Select(i => new WaveformChunk(i, 0, i is >= 150 and < 200 ? 0 : 0.5f)).ToArray());
        model.ModeIndex.Value = (int)SilenceSplitMode.RippleDelete;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var decoder = new Moq.Mock<IDecoderInfo>();
        decoder.Setup(d => d.IsSupported(path)).Returns(true);
        decoder.Setup(d => d.AudioExtensions()).Returns([".silence-retry"]);
        decoder.Setup(d => d.VideoExtensions()).Returns([]);
        decoder.Setup(d => d.Open(path, Moq.It.IsAny<MediaOptions>())).Returns(() =>
        {
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return null;
        });
        DecoderRegistry.Register(decoder.Object);
        Task applying = tab.ApplyAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (edit) audio.Start.Value = MediaTime.FromSeconds(1);
            release.Set();
            await applying;
            Assert.That(model.CanApply.Value, Is.EqualTo(!edit));
            Assert.That(model.Status.Value, Does.Contain(Strings.SilenceSplit_ShortSegment));
            Assert.That(scene.Children, Is.EqualTo(new[] { audio, video }));
            model.IncludeLinked.Value = false;
            await tab.ApplyAsync();
            Assert.That(scene.Children.Count, Is.EqualTo(edit ? 2 : 3));
        }
        finally
        {
            release.Set();
            await applying;
            DecoderRegistry.Unregister(decoder.Object);
        }
    }

    [AvaloniaTest]
    [TestCase(1200, "complete")]
    [TestCase(360, "complete")]
    [TestCase(1200, "fail")]
    [TestCase(1200, "earlier-fail")]
    [TestCase(360, "retarget")]
    [TestCase(360, "close")]
    public async Task ApplyProgress_RemainsVisibleUntilSaveFinishes(int width, string action)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(width == 360 ? "ja" : "en");
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("silence-progress");
        var audio = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        audio.Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "audio.belm"));
        audio.Objects.Add(new SourceSound { Source = { Value = SoundSource.Open(Path.Combine(TestShell.ProjectRoot(editor), "audio.wav")) } });
        scene.AddChild(audio);
        editor.HistoryManager.Commit("Setup");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        using (await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None)) { }
        int undoCount = editor.HistoryManager.UndoCount;
        using var tab = new SilenceSplitTabViewModel(editor);
        tab.Show(audio);
        var model = tab.Analysis.Value!;
        await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
        model.SetWaveform(Enumerable.Range(0, 500).Select(i =>
            new WaveformChunk(i, 0, i is >= 100 and < 150 or >= 300 and < 350 ? 0 : 0.5f)).ToArray());
        model.ModeIndex.Value = (int)SilenceSplitMode.RippleDelete;
        var view = new SilenceSplitTabView { DataContext = tab };
        var window = new Window { Content = view, Width = width, Height = width == 360 ? 680 : 360 };
        window.Show();
        HeadlessTestHelpers.Render();
        var bar = view.FindControl<ProgressBar>("OperationProgress")!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int writes = 0;
        var earlier = new Scene { Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "earlier.scene")) };
        bool earlierFailed = false;
        using var faults = StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
        {
            if (step == StorageWriteStep.Replace && path == earlier.Uri.LocalPath)
            {
                earlierFailed = true;
                throw new IOException("Unrelated earlier save failure");
            }
            if (step != StorageWriteStep.Replace || !path.EndsWith(".belm", StringComparison.Ordinal)
                || Interlocked.Increment(ref writes) != 2) return;
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            if (action == "fail") throw new IOException("Injected progress save failure");
        });
        using var status = model.Status.Subscribe(_ => Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True));
        Assert.That(model.CanApply.Value, Is.True, model.Status.Value);
        Task applying = tab.ApplyAsync();
        try
        {
            if (action == "earlier-fail")
            {
                using (await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None))
                    await ((AutoSaveService)editor.GetService(typeof(AutoSaveService))!).AutoSaveAsync(
                        [new UpdatePropertyValueOperation<string>(earlier, nameof(Scene.Name), "Earlier", "") { SequenceNumber = 1 }]);
                Assert.That(earlierFailed, Is.True);
            }
            Task first = await Task.WhenAny(started.Task, applying).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(first, Is.SameAs(started.Task), $"{model.Status.Value}; writes: {writes}");
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsBusy.Value && bar.Value is > 0 and < 100);
            HeadlessTestHelpers.Render();
            Assert.That(bar.IsVisible && bar.IsEffectivelyEnabled, Is.True);
            Assert.That(bar.IsIndeterminate, Is.False);
            Assert.That(tab.IsApplying.Value, Is.True);
            Assert.That(applying.IsCompleted, Is.False);
            Assert.That(model.Status.Value, Does.Contain(Strings.SilenceSplit_Saving));
            Assert.That(view.FindControl<Button>("ApplyButton")!.IsEffectivelyEnabled, Is.False);
            Assert.That(view.FindControl<Button>("RefreshButton")!.IsEffectivelyEnabled, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
            Point position = bar.TranslatePoint(default, window)!.Value;
            Assert.That(position.X + bar.Bounds.Width, Is.LessThanOrEqualTo(width));
            Assert.That(position.Y + bar.Bounds.Height, Is.LessThanOrEqualTo(window.Height));
            await Task.Delay(500); // Let progress-mode and button-color transitions settle before capture.
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(view, nameof(SilenceSplitTabTests), $"progress-saving-{width}-{action}");
            if (action is "retarget" or "close")
            {
                if (action == "retarget") tab.Show(audio); else tab.Dispose();
                await applying.WaitAsync(TimeSpan.FromSeconds(10));
            }
            release.Set();
            await applying;
            if (action == "retarget")
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => !tab.Analysis.Value!.IsBusy.Value);
                Assert.That(tab.Analysis.Value, Is.Not.SameAs(model));
                Assert.That(tab.Analysis.Value!.Status.Value, Does.Not.Contain(Strings.SilenceSplit_Saving));
            }
            else if (action != "close")
            {
                HeadlessTestHelpers.Render();
                Assert.That(model.IsBusy.Value || tab.IsApplying.Value || bar.IsVisible, Is.False);
                Assert.That(model.Status.Value, action == "fail"
                    ? Does.Contain("Injected progress save failure") : Is.EqualTo(Strings.Completed));
            }
        }
        finally
        {
            release.Set();
            await applying;
            window.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [AvaloniaTest]
    [TestCase("apply")]
    [TestCase("edit")]
    [TestCase("lock")]
    [TestCase("ungroup")]
    [TestCase("pending")]
    [TestCase("cancel")]
    public async Task BackgroundPreparation_LeavesUiResponsiveAndPublishesOnlyCurrentResults(string action)
    {
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("silence-background");
        string path = Path.Combine(TestShell.ProjectRoot(editor), "audio.silence-thread");
        File.WriteAllBytes(path, []);
        var audio = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        audio.Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "audio.belm"));
        audio.Objects.Add(new SourceSound { Source = { Value = SoundSource.Open(path) } });
        var video = new Element { Length = { Value = audio.Length.Value }, ZIndex = { Value = 1 } };
        video.Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "video.belm"));
        video.Objects.Add(new Beutl.Graphics.SourceVideo());
        scene.AddChild(audio);
        scene.AddChild(video);
        scene.Groups.Add([audio.Id.Value, video.Id.Value]);
        editor.HistoryManager.Commit("Setup");
        int undoCount = editor.HistoryManager.UndoCount;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        bool openedOnUi = false;
        var decoder = new Moq.Mock<IDecoderInfo>();
        decoder.Setup(d => d.IsSupported(path)).Returns(true);
        decoder.Setup(d => d.AudioExtensions()).Returns([".silence-thread"]);
        decoder.Setup(d => d.VideoExtensions()).Returns([]);
        decoder.Setup(d => d.Open(path, Moq.It.IsAny<MediaOptions>())).Returns(() =>
        {
            openedOnUi |= Dispatcher.UIThread.CheckAccess();
            started.TrySetResult();
            if (!openedOnUi && !release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return null;
        });
        DecoderRegistry.Register(decoder.Object);
        using var cancellation = new CancellationTokenSource();
        IDisposable? pendingMutation = null;
        Task<bool>? applying = null;
        try
        {
            applying = new SilenceSplitService(editor.HistoryManager).ApplyAsync(scene, audio,
                [new(MediaTime.FromSeconds(3), MediaTime.FromSeconds(1))], SilenceSplitMode.RippleDelete, true, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(openedOnUi, Is.False, "source metadata and fragment preparation belong to the worker");
            Assert.That(applying.IsCompleted, Is.False);
            Assert.That(scene.Children, Has.Count.EqualTo(2));
            Assert.That(audio.Length.Value.ToSeconds(), Is.EqualTo(10));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            await Dispatcher.UIThread.InvokeAsync(() => HeadlessTestHelpers.Render(), DispatcherPriority.Background);
            if (action == "edit") audio.Length.Value = MediaTime.FromSeconds(12);
            if (action == "lock") video.IsLocked.Value = true;
            if (action == "ungroup") scene.Groups.Clear();
            if (action == "pending") pendingMutation = editor.HistoryManager.BeforeMutation
                .Subscribe(_ => audio.Length.Value = MediaTime.FromSeconds(12));
            if (action == "cancel") cancellation.Cancel();
            release.Set();
            if (action == "cancel")
            {
                try { await applying; Assert.Fail("Canceled preparation must not publish its result."); }
                catch (OperationCanceledException) { }
            }
            else
                Assert.That(await applying, Is.EqualTo(action == "apply"));
            Assert.That(scene.Children.Count, Is.EqualTo(action == "apply" ? 4 : 2));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + (action == "apply" ? 1 : 0)));
            if (action == "apply")
            {
                editor.HistoryManager.Undo();
                Assert.That(scene.Children, Is.EquivalentTo(new[] { audio, video }));
                Assert.That(audio.Length.Value.ToSeconds(), Is.EqualTo(10));
                Assert.That(scene.Groups.Single(), Is.EquivalentTo(new[] { audio.Id.Value, video.Id.Value }));
            }
        }
        finally
        {
            release.Set();
            if (applying != null) { try { await applying; } catch (OperationCanceledException) { } }
            DecoderRegistry.Unregister(decoder.Object);
            pendingMutation?.Dispose();
        }
    }

    [AvaloniaTest]
    [TestCase(12)]
    [TestCase(8445, Explicit = true)]
    public async Task Apply_BatchUpdatesTimelineAndRetainsLinkedGroups(int silenceCount)
    {
        MediaTime duration = MediaTime.FromSeconds(21600);
        var (editor, timeline) = await TimelineTestHost.OpenTimelineAsync("silence-batch-timeline",
        [
            new(MediaTime.Zero, duration, 0, new ElementSource.EngineObject(() => new SourceSound())),
            new(MediaTime.Zero, duration, 1, new ElementSource.EngineObject(() => new Beutl.Graphics.SourceVideo()))
        ]);
        Scene scene = editor.Scene;
        scene.Duration.Value = duration;
        Element[] original = scene.Children.ToArray();
        scene.Groups.Add([.. original.Select(e => e.Id.Value)]);
        editor.HistoryManager.Commit("Setup");
        timeline.ClearSelected();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1000, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            var ranges = Enumerable.Range(0, silenceCount).Select(i => TimeRange.FromRange(
                MediaTime.FromSeconds(3 + i * 2.5), MediaTime.FromSeconds(4 + i * 2.5))).ToArray();
            int undoCount = editor.HistoryManager.UndoCount;
            var watch = Stopwatch.StartNew();
            await new SilenceSplitService(editor.HistoryManager).ApplyAsync(scene, original[0], ranges, SilenceSplitMode.RippleDelete, true);
            TestContext.Out.WriteLine($"Apply with open timeline / {silenceCount} silences / linked audio + video: {watch.Elapsed.TotalSeconds:F2} s");
            HeadlessTestHelpers.Render();
            using (await TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None)) { }
            TestContext.Out.WriteLine($"Including timeline render and auto-save: {watch.Elapsed.TotalSeconds:F2} s");
            Assert.That(timeline.Elements, Has.Count.EqualTo((silenceCount + 1) * 2));
            Assert.That(view.GetVisualDescendants().OfType<ElementView>().Count(), Is.LessThan(40),
                "off-screen fragments must not allocate a control tree");
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
            ElementViewModel fragment = timeline.Elements[2];
            var linked = fragment.GetGroupOrSelectedElements();
            Assert.That(linked, Has.Count.EqualTo(2));
            Assert.That(linked.All(e => e.Model.Range == fragment.Model.Range), Is.True);
            view.FindControl<ScrollViewer>("ContentScroll")!.Offset = new Vector(fragment.BorderMargin.Value.Left, 0);
            HeadlessTestHelpers.Render();
            Assert.That(view.GetVisualDescendants().OfType<ElementView>().Any(v => v.DataContext == fragment), Is.True,
                "scrolling must realize the newly visible clip");
            HeadlessCapture.SaveIfRequested(view, nameof(SilenceSplitTabTests), $"batch-{silenceCount}");

            // Existing view models must observe membership changes and their undo without caching old groups.
            var structure = (IElementStructureService)editor.GetService(typeof(IElementStructureService))!;
            structure.Ungroup(scene, linked.Select(e => e.Model.Id.Value).ToArray());
            Assert.That(fragment.GetGroupOrSelectedElements(), Is.Empty);
            editor.HistoryManager.Undo();
            Assert.That(fragment.GetGroupOrSelectedElements(), Is.EquivalentTo(linked));
            editor.HistoryManager.Undo();
            HeadlessTestHelpers.Render();
            Assert.That(timeline.Elements.Select(e => e.Model), Is.EqualTo(original));
            Assert.That(original.All(e => e.Length.Value == duration), Is.True);
            Assert.That(timeline.Elements[0].GetGroupOrSelectedElements(), Has.Count.EqualTo(2));
        }
        finally
        {
            TimelineTestHost.Close(view, window);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ElementMenu_OpensTheClickedAudioAndReusesTheHiddenToolTab(bool grouped)
    {
        Sound Audio() => grouped ? new SoundGroup { Children = { new SoundGroup { Children = { CreateSound() } } } } : CreateSound();
        var (editor, timeline) = await TimelineTestHost.OpenTimelineAsync("silence-menu",
        [
            new(MediaTime.Zero, MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(Audio)),
            new(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(Audio)),
            new(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() =>
                new SceneDrawable { ReferencedScene = { Value = new Scene { Children = { new Element { Objects = { CreateSound() } } } } } }))
        ]);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 1000, Height = 420 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            var elements = editor.Scene.Children.OrderBy(e => e.Start.Value).ToArray();
            ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(elements[1]);
            OpenMenu(elements[0], true, click: true);
            var tab = editor.FindToolTab<SilenceSplitTabViewModel>()!;
            Assert.That(tab.Target, Is.SameAs(elements[0]), "the clicked element is independent of the current selection");
            Assert.That(SilenceSplitTabExtension.Instance.Header, Is.Null);
            Assert.That(TestShell.MainViewModel.MenuItems.Single(item => item.Header == Strings.Tools).Items!
                .Any(item => item.Header == Strings.AutoSplitBySilence), Is.False);
            OpenMenu(elements[1], true, click: true);
            Assert.That(editor.FindToolTab<SilenceSplitTabViewModel>(), Is.SameAs(tab));
            Assert.That(tab.Target, Is.SameAs(elements[1]));
            OpenMenu(elements[2], false, click: false);
            elements[0].IsLocked.Value = true;
            OpenMenu(elements[0], false, click: false);

            void OpenMenu(Element target, bool enabled, bool click)
            {
                HeadlessTestHelpers.Render();
                var element = view.GetVisualDescendants().OfType<ElementView>()
                    .Single(v => v.DataContext is ElementViewModel vm && vm.Model == target);
                var flyout = (FAMenuFlyout)element.FindControl<Border>("border")!.ContextFlyout!;
                flyout.ShowAt(element);
                HeadlessTestHelpers.Render(3);
                var item = element.FindControl<FAMenuFlyoutItem>("autoSplitBySilence")!;
                Assert.That(item.IsEnabled, Is.EqualTo(enabled));
                HeadlessCapture.SaveIfRequested(element, nameof(SilenceSplitTabTests), $"audio-menu-{grouped}-{enabled}");
                if (click) item.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
                flyout.Hide();
            }
        }
        finally
        {
            if (editor.FindToolTab<SilenceSplitTabViewModel>() is { } tab) editor.CloseToolTab(tab);
            TimelineTestHost.Close(view, window);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ApplyButton_AwaitsPlaybackAndRequiresRefreshAfterEditing(bool editDuringPause)
    {
        var scene = new Scene(100, 100, "silence") { Uri = new Uri(Path.Combine(Path.GetTempPath(), "silence-ui.scene")) };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        element.Objects.Add(CreateSound());
        scene.Children.Add(element);
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(sequence);
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        history.Subscribe(observer);
        var player = new BlockingPreviewPlayer();
        using var tab = new SilenceSplitTabViewModel(new EditorContext(scene, history, player));
        tab.Show(element);
        var model = tab.Analysis.Value!;
        var view = new SilenceSplitTabView { DataContext = tab };
        var window = new Window { Content = view, Width = 360, Height = 680 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(30);
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
            model.SetWaveform(Enumerable.Range(0, 500).Select(i => new WaveformChunk(i, 0, i is >= 100 and < 250 ? 0 : 0.5f)).ToArray());
            model.ModeIndex.Value = 2;
            HeadlessTestHelpers.Render();
            var button = view.FindControl<Button>("ApplyButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await HeadlessTestHelpers.WaitUntilAsync(() => player.PauseStarted);
            Assert.That(button.IsEffectivelyEnabled, Is.False);
            var progress = view.FindControl<ProgressBar>("OperationProgress")!;
            Assert.That(progress.IsVisible && progress.IsIndeterminate && progress.IsEffectivelyEnabled, Is.True);
            Assert.That(history.UndoCount, Is.Zero);
            Assert.That(scene.Children, Is.EqualTo(new[] { element }));
            if (editDuringPause) element.Length.Value = MediaTime.FromSeconds(12);
            player.CompletePause();
            await HeadlessTestHelpers.WaitUntilAsync(() => !tab.IsApplying.Value);
            HeadlessTestHelpers.Render();
            Assert.That(progress.IsVisible, Is.False);
            bool applied = !editDuringPause;
            Assert.That(history.UndoCount, Is.EqualTo(applied ? 1 : 0));
            Assert.That(scene.Children.Count, Is.EqualTo(applied ? 2 : 1));
            Assert.That(model.CanApply.Value, Is.False);
            model.PaddingMilliseconds.Value = 200;
            Assert.That(model.CanApply.Value, Is.False, "changing parameters cannot reapply an old preview");
            if (applied) history.Undo();
            Assert.That(scene.Children, Is.EqualTo(new[] { element }));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(editDuringPause ? 12 : 10)));
            view.FindControl<Button>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var refreshed = tab.Analysis.Value!;
            Assert.That(refreshed, Is.Not.SameAs(model));
            await HeadlessTestHelpers.WaitUntilAsync(() => !refreshed.IsBusy.Value);
            Assert.That(refreshed.ClipRange, Is.EqualTo(element.Range));
            Assert.That(refreshed.PaddingMilliseconds.Value, Is.EqualTo(200));
            Assert.That(refreshed.CanApply.Value, Is.True);
        }
        finally
        {
            player.CompletePause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ClosingOrRetargetingDuringPausePreventsOldEdits(bool retarget)
    {
        var scene = new Scene(100, 100, "silence") { Uri = new Uri(Path.Combine(Path.GetTempPath(), "silence-pause.scene")) };
        var first = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        var second = new Element { Start = { Value = MediaTime.FromSeconds(12) }, Length = { Value = MediaTime.FromSeconds(10) } };
        first.Objects.Add(CreateSound());
        second.Objects.Add(CreateSound());
        scene.Children.AddRange([first, second]);
        using var history = new HistoryManager(new OperationSequenceGenerator());
        var player = new BlockingPreviewPlayer();
        using var tab = new SilenceSplitTabViewModel(new EditorContext(scene, history, player));
        tab.Show(first);
        await HeadlessTestHelpers.WaitUntilAsync(() => !tab.Analysis.Value!.IsBusy.Value);
        Task applying = tab.ApplyAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => player.PauseStarted);
        if (retarget) tab.Show(second); else tab.Dispose();
        player.CompletePause();
        await applying;
        Assert.That(scene.Children, Is.EqualTo(new[] { first, second }));
        Assert.That(first.Length.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
        if (retarget) Assert.That(tab.Target, Is.SameAs(second));
    }

    [AvaloniaTest]
    [TestCase("en", 400, false)]
    [TestCase("ja", 360, true)]
    [TestCase("zh-CN", 320, true)]
    [TestCase("ko-KR", 360, false)]
    [TestCase("es", 360, true)]
    public async Task Preview_ReactsToParameters_AndTabFits(string language, int width, bool light)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var scene = new Scene(100, 100, "silence");
        var element = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        element.Name.Value = "Narration.wav";
        element.Objects.Add(CreateSound());
        var linked = new Element { Length = { Value = MediaTime.FromSeconds(10) }, ZIndex = { Value = 1 } };
        scene.Children.Add(element);
        scene.Children.Add(linked);
        scene.Groups.Add([element.Id.Value, linked.Id.Value]);
        using var history = new HistoryManager(new OperationSequenceGenerator());
        using var tab = new SilenceSplitTabViewModel(new EditorContext(scene, history, new BlockingPreviewPlayer()));
        tab.Show(element);
        var model = tab.Analysis.Value!;
        var view = new SilenceSplitTabView { DataContext = tab };
        var window = new Window { Content = view, Width = width, Height = 720, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(30);
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
            model.SetWaveform(Enumerable.Range(0, 500).Select(i => new WaveformChunk(i,
                i is >= 100 and < 250 ? -0.005f : -0.55f,
                i is >= 100 and < 250 ? 0.005f : 0.65f)).ToArray());
            HeadlessTestHelpers.Render();
            Assert.That(model.Ranges, Has.Count.EqualTo(1));
            var apply = view.FindControl<Button>("ApplyButton")!;
            Assert.That(apply.IsEffectivelyEnabled, Is.True);
            view.FindControl<NumericUpDown>("Threshold")!.Value = null;
            HeadlessTestHelpers.Render();
            Assert.That(apply.IsEffectivelyEnabled, Is.False, "an empty threshold must not apply the previous value");
            view.FindControl<NumericUpDown>("Threshold")!.Value = -50;
            HeadlessTestHelpers.Render();
            Assert.That(model.Ranges, Is.Empty);
            Assert.That(apply.IsEffectivelyEnabled, Is.False);
            view.FindControl<NumericUpDown>("Threshold")!.Value = -40;
            view.FindControl<ComboBox>("Mode")!.SelectedIndex = 2;
            view.FindControl<CheckBox>("Linked")!.IsChecked = false;
            HeadlessTestHelpers.Render();
            Assert.That(model.ModeIndex.Value, Is.EqualTo(2));
            Assert.That(model.IncludeLinked.Value, Is.False);
            Assert.That(model.Ranges, Has.Count.EqualTo(1));
            Assert.That(model.Ranges[0].Duration.ToSeconds(), Is.EqualTo(2.8).Within(0.001));
            foreach (var control in view.GetVisualDescendants().OfType<NumericUpDown>())
            {
                var point = control.TranslatePoint(default, window)!.Value;
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(point.X + control.Bounds.Width, Is.LessThanOrEqualTo(width));
            }
            // Moving between docks detaches the view, but must retain the analysis and options.
            window.Content = null;
            window.Content = view;
            HeadlessTestHelpers.Render();
            Assert.That(tab.Analysis.Value, Is.SameAs(model));
            Assert.That(apply.IsEffectivelyEnabled, Is.True);
            HeadlessCapture.SaveIfRequested(view, nameof(SilenceSplitTabTests), $"{language}-{width}");
            element.Length.Value = MediaTime.FromSeconds(12);
            Assert.That(model.CanApply.Value, Is.False, "a stale preview cannot edit the changed clip");
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [AvaloniaTest]
    [TestCase("en", 1200, 360)]
    [TestCase("ja", 1200, 360)]
    [TestCase("es", 900, 320)]
    public async Task LongRecording_CanNavigateAndResizeBetweenWideAndNarrowTabs(string language, int width, int height)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var scene = new Scene(100, 100, "long-recording");
        var element = new Element { Start = { Value = MediaTime.FromSeconds(10) }, Length = { Value = MediaTime.FromSeconds(1800) } };
        element.Name.Value = "Narration-30min.wav";
        // Fail the initial read immediately, then supply deterministic bins without decoding a large file in a layout test.
        element.Objects.Add(new SourceSound { Source = { Value = new SoundSource() } });
        scene.Children.Add(element);
        var linked = new Element { Start = { Value = element.Start.Value }, Length = { Value = element.Length.Value } };
        scene.Children.Add(linked);
        scene.Groups.Add([element.Id.Value, linked.Id.Value]);
        using var history = new HistoryManager(new OperationSequenceGenerator());
        var editor = new EditorContext(scene, history, new BlockingPreviewPlayer());
        using var tab = new SilenceSplitTabViewModel(editor);
        tab.Show(element);
        var model = tab.Analysis.Value!;
        var view = new SilenceSplitTabView { DataContext = tab };
        var window = new Window { Content = view, Width = width, Height = height, RequestedThemeVariant = language == "en" ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
            model.SetWaveform(Enumerable.Range(0, 90000).Select(i =>
            {
                float peak = i / 50 % 5 == 4 ? 0.002f : 0.2f + 0.5f * MathF.Abs(MathF.Sin(i * 0.031f));
                return new WaveformChunk(i, -peak, peak);
            }).ToArray());
            var ranges = model.Ranges;
            Assert.That(ranges, Has.Count.EqualTo(360));
            var zoomIn = view.FindControl<Button>("ZoomInButton")!;
            for (int i = 0; i < 6; i++) zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<ScrollBar>("PreviewScroll")!.Value = 1200;
            HeadlessTestHelpers.Render();
            Assert.That(model.PreviewRange.Start, Is.EqualTo(MediaTime.FromSeconds(1200)));
            Assert.That(model.PreviewRange.Duration.ToSeconds(), Is.LessThan(30));
            Assert.That(model.PreviewChunks.Count, Is.LessThanOrEqualTo(1200));
            Assert.That(model.Ranges, Is.SameAs(ranges));
            Assert.That(view.FindControl<TextBlock>("PreviewStartTime")!.Text, Is.EqualTo("00:20:00.00"));
            var range = model.PreviewRange;
            editor.Clock.Seek(element.Start.Value + range.Start + new MediaTime(range.Duration.Flicks / 3));
            AssertWideLayout();
            Assert.That(view.FindControl<Border>("Playhead")!.IsVisible, Is.True);
            // Allow the enabled-button color transition to settle before the design capture.
            await Task.Delay(200);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(view, nameof(SilenceSplitTabTests), $"{language}-wide-{width}x{height}");
            var waveformPosition = view.FindControl<WaveformControl>("Waveform")!.TranslatePoint(default, window);
            view.FindControl<CheckBox>("Linked")!.BringIntoView();
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<WaveformControl>("Waveform")!.TranslatePoint(default, window), Is.EqualTo(waveformPosition),
                "scrolling settings keeps the waveform visible");

            window.Width = 360;
            window.Height = 760;
            HeadlessTestHelpers.Render();
            var options = view.FindControl<ScrollViewer>("OptionsScroll")!;
            Assert.That(Grid.GetRow(options), Is.EqualTo(1));
            options.BringIntoView();
            HeadlessTestHelpers.Render();
            Assert.That(model.PreviewRange, Is.EqualTo(range));
            window.Width = width;
            window.Height = height;
            HeadlessTestHelpers.Render();
            AssertWideLayout();
            Assert.That(model.PreviewRange, Is.EqualTo(range));
            view.FindControl<ScrollBar>("PreviewScroll")!.Value = double.MaxValue;
            Assert.That(model.PreviewRange.End, Is.EqualTo(element.Length.Value));
            view.FindControl<Button>("ResetZoomButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(model.PreviewRange, Is.EqualTo(new TimeRange(MediaTime.Zero, element.Length.Value)));
            Assert.That(model.Ranges, Is.SameAs(ranges));

            void AssertWideLayout()
            {
                var waveform = view.FindControl<WaveformControl>("Waveform")!;
                var controls = view.FindControl<ScrollViewer>("OptionsScroll")!;
                var apply = view.FindControl<Button>("ApplyButton")!;
                Assert.That(Grid.GetColumn(controls), Is.EqualTo(1));
                Assert.That(waveform.Bounds.Width, Is.GreaterThan(controls.Bounds.Width));
                var mode = view.FindControl<ComboBox>("Mode")!;
                Assert.That(mode.TranslatePoint(default, controls)!.Value.Y + mode.Bounds.Height,
                    Is.LessThanOrEqualTo(controls.Bounds.Height), "the apply mode fits inside the settings viewport");
                foreach (Control control in new Control[] { waveform, apply, view.FindControl<NumericUpDown>("Threshold")!, view.FindControl<ComboBox>("Mode")! })
                {
                    var point = control.TranslatePoint(default, window)!.Value;
                    Assert.That(point.Y, Is.GreaterThanOrEqualTo(0));
                    Assert.That(point.Y + control.Bounds.Height, Is.LessThanOrEqualTo(height));
                }
                Assert.That(apply.IsEffectivelyEnabled, Is.True);
            }
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control, 1200)]
    [TestCase(RawInputModifiers.Meta, 360)]
    public async Task Preview_WheelZoomWorksInEmptySpaceAndKeepsPointerTime(RawInputModifiers modifier, int width)
    {
        using var session = await PreviewSession.CreateAsync(width);
        var model = session.Model;
        var waveform = session.View.FindControl<WaveformControl>("Waveform")!;
        var ranges = model.Ranges;
        // All samples are silent: neither the top nor the bottom of the waveform draws a bar.
        foreach (double y in new[] { 2d, waveform.Bounds.Height - 2 })
        {
            model.ResetPreview();
            double anchor = model.PreviewRange.Duration.ToSeconds() * 0.25;
            session.Scroll(waveform, new Point(waveform.Bounds.Width * 0.25, y), new Vector(0, 0.5), modifier);
            var range = model.PreviewRange;
            Assert.That(60 / range.Duration.ToSeconds(), Is.EqualTo(Math.Pow(1.2, 0.5)).Within(0.001));
            Assert.That(range.Start.ToSeconds() + range.Duration.ToSeconds() * 0.25, Is.EqualTo(anchor).Within(0.02));
            Assert.That(session.View.FindControl<ScrollViewer>("BodyScroll")!.Offset, Is.EqualTo(default(Vector)));
        }
        // The scrollbar handles wheel events itself; preview zoom must get them first.
        var scroll = session.View.FindControl<ScrollBar>("PreviewScroll")!;
        double previous = model.PreviewRange.Duration.ToSeconds();
        session.Scroll(scroll, new Point(scroll.Bounds.Width * 0.5, scroll.Bounds.Height * 0.5), new Vector(0, 1), modifier);
        Assert.That(model.PreviewRange.Duration.ToSeconds(), Is.LessThan(previous));
        model.ResetPreview();
        model.ZoomPreview(3000d / 64, 0.5);
        previous = model.PreviewRange.Duration.ToSeconds();
        for (int i = 0; i < 20; i++)
            session.Scroll(waveform, new Point(waveform.Bounds.Width * 0.5, 2), new Vector(0, 0.02), modifier);
        Assert.That(model.PreviewRange.Duration.ToSeconds(), Is.LessThan(previous), "fractional wheel input must accumulate");
        Assert.That(model.Ranges, Is.SameAs(ranges));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Preview_ScrollUsesTimelineAxesAndDoesNotZoom(bool gesture, bool swap)
    {
        bool original = GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value = swap;
            using var session = await PreviewSession.CreateAsync(1200, _ => gesture);
            var model = session.Model;
            var waveform = session.View.FindControl<WaveformControl>("Waveform")!;
            model.ZoomPreview(true);
            foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
            {
                foreach (var delta in new[] { new Vector(0, -1), new Vector(-0.4, -0.7) })
                {
                    model.PanPreview(10);
                    bool swapAxes = !gesture && (!swap ^ (OperatingSystem.IsWindows() && modifiers == RawInputModifiers.Shift));
                    double expected = 10 - (swapAxes ? delta.Y : delta.X) * 50 * 30 / waveform.Bounds.Width;
                    session.Scroll(waveform, new Point(100, 2), delta, modifiers);
                    Assert.That(model.PreviewRange.Start.ToSeconds(), Is.EqualTo(expected).Within(0.02));
                    Assert.That(model.PreviewRange.Duration, Is.EqualTo(MediaTime.FromSeconds(30)));
                }
            }
            model.PanPreview(0);
            session.Scroll(waveform, new Point(100, 2), new Vector(100, 100));
            Assert.That(model.PreviewRange.Start, Is.EqualTo(MediaTime.Zero));
            while (model.CanZoomIn) model.ZoomPreview(true);
            model.PanPreview(10);
            var fineDelta = gesture || swap ? new Vector(-0.1, 0) : new Vector(0, -0.1);
            for (int i = 0; i < 20; i++) session.Scroll(waveform, new Point(100, 2), fineDelta);
            Assert.That(model.PreviewRange.Start.ToSeconds(), Is.EqualTo(10 + 100 / waveform.Bounds.Width).Within(0.02),
                "sub-bin touchpad motion must accumulate at high zoom");
        }
        finally { GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value = original; }
    }

    [AvaloniaTest]
    public async Task Preview_PlayheadFollowsClockZoomPanAndReattachment()
    {
        using var session = await PreviewSession.CreateAsync(1200);
        var model = session.Model;
        var clock = session.Editor.Clock;
        var waveform = session.View.FindControl<WaveformControl>("Waveform")!;
        var playhead = session.View.FindControl<Border>("Playhead")!;
        clock.Seek(MediaTime.FromSeconds(25)); // Clip starts at 10 s.
        AssertPosition(0.25);
        var preview = model.PreviewChunks;
        clock.Seek(MediaTime.FromSeconds(40));
        AssertPosition(0.5);
        Assert.That(model.PreviewChunks, Is.SameAs(preview), "clock ticks must not rebuild the waveform");
        model.ZoomPreview(true);
        AssertPosition(0.5);
        model.PanPreview(30);
        AssertPosition(0);
        clock.Seek(MediaTime.FromSeconds(39));
        Assert.That(playhead.IsVisible, Is.False);
        clock.Seek(MediaTime.FromSeconds(71));
        Assert.That(playhead.IsVisible, Is.False);
        session.Window.Content = null;
        clock.Seek(MediaTime.FromSeconds(55));
        session.Window.Content = session.View;
        session.Window.Width = 360;
        HeadlessTestHelpers.Render();
        AssertPosition(0.5);

        void AssertPosition(double fraction)
        {
            HeadlessTestHelpers.Render();
            Assert.That(playhead.IsVisible, Is.True);
            Assert.That(Canvas.GetLeft(playhead), Is.EqualTo(waveform.Bounds.Width * fraction).Within(1));
            Assert.That(playhead.Height, Is.EqualTo(waveform.Bounds.Height));
        }
    }

    [AvaloniaTest]
    [TestCase("ja", 360)]
    [TestCase("en", 1000)]
    public async Task TabState_RestoresOptionsWithoutDecodingUntilRefresh(string language, int width)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var scene = new Scene(100, 100, "silence");
        var element = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        element.Objects.Add(CreateSound());
        scene.Children.Add(element);
        using var history = new HistoryManager(new OperationSequenceGenerator());
        var editor = new EditorContext(scene, history, new BlockingPreviewPlayer());
        using var tab = new SilenceSplitTabViewModel(editor);
        tab.Show(element);
        tab.Analysis.Value!.ThresholdDb.Value = -35;
        tab.Analysis.Value.ModeIndex.Value = 2;
        var json = new JsonObject();
        tab.WriteToJson(json);
        using var restored = new SilenceSplitTabViewModel(editor);
        restored.ReadFromJson(json);
        var view = new SilenceSplitTabView { DataContext = restored };
        var window = new Window { Content = view, Width = width, Height = 500 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var model = restored.Analysis.Value!;
            Assert.That(restored.Target, Is.SameAs(element));
            Assert.That(model.ThresholdDb.Value, Is.EqualTo(-35));
            Assert.That(model.ModeIndex.Value, Is.EqualTo(2));
            Assert.That(model.IsBusy.Value, Is.False);
            Assert.That(model.PreviewChunks, Is.Empty);
            Assert.That(model.Status.Value, Is.EqualTo(Strings.SilenceSplit_RefreshToAnalyze));
            Assert.That(view.FindControl<Button>("ApplyButton")!.IsEnabled, Is.False);
            HeadlessCapture.SaveIfRequested(view, nameof(SilenceSplitTabTests), $"restore-{language}-{width}");

            view.FindControl<Button>("RefreshButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await HeadlessTestHelpers.WaitUntilAsync(() => !restored.Analysis.Value!.IsBusy.Value);
            Assert.That(restored.Analysis.Value!.PreviewChunks, Is.Not.Empty);
            Assert.That(restored.Analysis.Value.ThresholdDb.Value, Is.EqualTo(-35));
            Assert.That(restored.Analysis.Value.ModeIndex.Value, Is.EqualTo(2));
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static SceneSound CreateSound() => new()
    {
        ReferencedScene = { Value = new Scene(100, 100, "audio") { Duration = { Value = MediaTime.FromSeconds(10) } } }
    };

    private sealed class EditorContext(Scene scene, HistoryManager history, IPreviewPlayer player) : IEditorContext
    {
        public TestClock Clock { get; } = new();
        public CoreObject Object => scene;
        public EditorExtension Extension => null!;
        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);
        public object? GetService(Type type) => type == typeof(Scene) ? scene : type == typeof(HistoryManager) ? history
            : type == typeof(IPreviewPlayer) ? player : type == typeof(IEditorClock) ? Clock : null;
        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;
        public T? FindToolTab<T>() where T : IToolContext => default;
        public bool OpenToolTab(IToolContext item) => false;
        public void CloseToolTab(IToolContext item) => item.Dispose();
    }

    private sealed class TestClock : IEditorClock
    {
        private readonly ReactivePropertySlim<MediaTime> _time = new();
        public IReadOnlyReactiveProperty<MediaTime> CurrentTime => _time;
        public IReadOnlyReactiveProperty<MediaTime> MaximumTime { get; } = new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(70));
        public void Seek(MediaTime time) => _time.Value = time;
    }

    private sealed class PreviewSession : IDisposable
    {
        private readonly HistoryManager _history = new(new OperationSequenceGenerator());
        public EditorContext Editor { get; }
        public SilenceSplitTabViewModel Tab { get; }
        public SilenceSplitViewModel Model => Tab.Analysis.Value!;
        public SilenceSplitTabView View { get; }
        public Window Window { get; }

        private PreviewSession(int width, Func<PointerWheelEventArgs, bool>? detector)
        {
            var scene = new Scene(100, 100, "navigation");
            var element = new Element { Start = { Value = MediaTime.FromSeconds(10) }, Length = { Value = MediaTime.FromSeconds(60) } };
            element.Objects.Add(new SourceSound { Source = { Value = new SoundSource() } });
            scene.Children.Add(element);
            Editor = new EditorContext(scene, _history, new BlockingPreviewPlayer());
            Tab = new SilenceSplitTabViewModel(Editor);
            Tab.Show(element);
            View = detector == null ? new SilenceSplitTabView() : new SilenceSplitTabView(detector);
            View.DataContext = Tab;
            Window = new Window { Content = View, Width = width, Height = width >= 800 ? 360 : 720 };
            Window.Show();
        }

        public static async Task<PreviewSession> CreateAsync(int width, Func<PointerWheelEventArgs, bool>? detector = null)
        {
            var session = new PreviewSession(width, detector);
            await HeadlessTestHelpers.WaitUntilAsync(() => !session.Model.IsBusy.Value);
            session.Model.SetWaveform(Enumerable.Range(0, 3000).Select(i => new WaveformChunk(i, 0, 0)).ToArray());
            session.Model.MinimumMilliseconds.Value = 120000; // Leave the waveform background unpainted by highlights.
            HeadlessTestHelpers.Render();
            return session;
        }

        public void Scroll(Control target, Point point, Vector delta, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.MouseWheel(target.TranslatePoint(point, Window)!.Value, delta, modifiers);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            Window.Close();
            Tab.Dispose();
            _history.Dispose();
        }
    }

    private sealed class BlockingPreviewPlayer : IPreviewPlayer
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool PauseStarted { get; private set; }
        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; } = new ReactivePropertySlim<Ref<Bitmap>?>();
        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();
        public IReadOnlyReactiveProperty<bool> IsPlaying { get; } = new ReactivePropertySlim<bool>(true);

        public Task Pause()
        {
            PauseStarted = true;
            return _paused.Task;
        }

        public void CompletePause() => _paused.TrySetResult();
    }
}
