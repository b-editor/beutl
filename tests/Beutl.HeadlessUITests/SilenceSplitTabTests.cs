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
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SilenceSplitTabTests
{
    [AvaloniaTest]
    public async Task ElementMenu_OpensTheClickedAudioAndReusesTheHiddenToolTab()
    {
        var (editor, timeline) = await TimelineTestHost.OpenTimelineAsync("silence-menu",
        [
            new(MediaTime.Zero, MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(CreateSound)),
            new(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(CreateSound)),
            new(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.RectShape()))
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
            Assert.That(history.UndoCount, Is.Zero);
            Assert.That(scene.Children, Is.EqualTo(new[] { element }));
            if (editDuringPause) element.Length.Value = MediaTime.FromSeconds(12);
            player.CompletePause();
            await HeadlessTestHelpers.WaitUntilAsync(() => !tab.IsApplying.Value);
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
    public async Task TabState_RestoresTheTargetAndOptions()
    {
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
        await HeadlessTestHelpers.WaitUntilAsync(() => !restored.Analysis.Value!.IsBusy.Value);
        Assert.That(restored.Target, Is.SameAs(element));
        Assert.That(restored.Analysis.Value!.ThresholdDb.Value, Is.EqualTo(-35));
        Assert.That(restored.Analysis.Value.ModeIndex.Value, Is.EqualTo(2));
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
