using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ProxiesTab.ViewModels;
using Beutl.Editor.Components.ProxiesTab.Views;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;
using PixelSize = Beutl.Media.PixelSize;
using Point = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ProxiesTabLayoutTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(320, true)]
    [TestCase(640, false)]
    [TestCase(640, true)]
    public async Task Clip_states_and_actions_remain_accessible_in_compact_layouts(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"proxy-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "scene.scene")) };
        var store = new ProxyStore(Path.Combine(root, "proxy-store"));
        var queue = new TestQueue();
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(Scene))).Returns(scene);
        context.Setup(x => x.GetService(typeof(IProxyStore))).Returns(store);
        context.Setup(x => x.GetService(typeof(IProxyJobQueue))).Returns(queue);
        using var model = new ProxiesTabViewModel(context.Object);
        var view = new ProxiesTabView { DataContext = model };
        object? tappedSource = null;
        view.AddHandler(InputElement.TappedEvent, (_, e) => tappedSource = e.Source,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        var window = new Window
        {
            Content = view, Width = width, Height = 600,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(view.FindControl<Border>("EmptyState")!.IsEffectivelyVisible, Is.True);
            Capture("empty");

            string readyPath = AddSource("forest-a-long-descriptive-clip-name-for-layout.mov", ProxyState.Ready);
            AddSource("city.mov", ProxyState.Stale);
            AddSource("interview.mov", null);
            AddSource("failed.mov", ProxyState.Failed);
            HeadlessTestHelpers.Render(3);
            Assert.That(model.Clips, Has.Count.EqualTo(4));
            Assert.That(model.HasClips.Value, Is.True);
            Assert.That(model.HasSelection.Value, Is.False);
            Assert.That(view.FindControl<Border>("EmptyState")!.IsEffectivelyVisible, Is.False);
            Button generateSelected = view.FindControl<Button>("GenerateButton")!;
            Assert.That(generateSelected.IsEffectivelyEnabled, Is.True);
            Assert.That(generateSelected.Content, Is.EqualTo(Strings.ProxyGenerateAll));
            Assert.That(generateSelected.Command, Is.SameAs(model.GenerateAllCommand));
            CheckHorizontalBounds();
            Capture("clips");
            FindRow(model.Clips.Single(x => x.IsFailed.Value)).BringIntoView();
            HeadlessTestHelpers.Render();
            Capture("failed");

            ProxyClipViewModel clip = FindClip();
            Border row = FindRow(clip);
            Click(Part<TextBlock>(row, "FileNameText"));
            Assert.That(clip.IsSelected.Value, Is.True);
            Assert.That(model.HasSelection.Value, Is.True);
            Assert.That(generateSelected.IsEffectivelyEnabled, Is.True);
            Assert.That(generateSelected.Content, Is.EqualTo(Strings.ProxyGenerateSelected));
            Assert.That(generateSelected.Command, Is.SameAs(model.GenerateSelectedCommand));
            Click(generateSelected);
            Assert.That(queue.Enqueued, Has.Count.EqualTo(1));
            Assert.That(queue.Enqueued[0].Source, Is.EqualTo(ProxyFingerprint.FromFile(readyPath)));
            Assert.That(queue.Enqueued[0].Priority, Is.EqualTo(1));

            queue.ReportProgress(queue.Enqueued[0], 0.42);
            HeadlessTestHelpers.Render();
            row = FindRow(FindClip());
            Assert.That(Part<StackPanel>(row, "JobProgressPanel").IsEffectivelyVisible, Is.True);
            Assert.That(FindClip().JobProgressValue.Value, Is.EqualTo(0.42));
            row.BringIntoView();
            HeadlessTestHelpers.Render();
            Capture("generating");
            Click(Part<Button>(row, "CancelJobButton"));
            Assert.That(queue.Canceled, Does.Contain(queue.Enqueued[0].JobId));
            Assert.That(FindClip().HasJob.Value, Is.False);

            row = FindRow(FindClip());
            ToggleButton details = Part<ToggleButton>(row, "DetailsToggle");
            details.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Render(3);
            window.UpdateLayout();
            Assert.That(Part<StackPanel>(row, "DetailsPanel").IsEffectivelyVisible, Is.True);
            Assert.That(FindClip().IsSelected.Value, Is.True, "Selection must survive cancellation and expanding details.");
            Capture("expanded");
            Click(row.GetVisualDescendants().OfType<SelectableTextBlock>().Single());
            Assert.That(FindClip().IsSelected.Value, Is.True, $"Selecting a file path must not toggle the row selection. Tap source: {tappedSource}");
            Capture("details");
            ComboBox quality = Part<ComboBox>(row, "QualityComboBox");
            quality.SelectedIndex = 0;
            HeadlessTestHelpers.Render();
            Assert.That(FindClip().Preset.Value, Is.EqualTo(ProxyPreset.Half));
            Assert.That(FindClip().IsSelected.Value, Is.True);
            quality.SelectedIndex = 1;
            HeadlessTestHelpers.Render();

            Button more = view.FindControl<Button>("MoreActionsButton")!;
            Click(more);
            var menu = (MenuFlyout)more.Flyout!;
            MenuItem[] actions = menu.Items.OfType<MenuItem>().ToArray();
            Assert.That(actions.Select(x => x.Command), Is.EquivalentTo(new System.Windows.Input.ICommand[]
            {
                model.GenerateAllCommand, model.RegenerateSelectedCommand,
                model.DeleteSelectedCommand, model.DeleteAllForProjectCommand
            }));
            Click(actions.Single(x => ReferenceEquals(x.Command, model.RegenerateSelectedCommand)));
            Assert.That(queue.Enqueued, Has.Count.EqualTo(2));
            Click(more);
            Click(actions.Single(x => ReferenceEquals(x.Command, model.DeleteSelectedCommand)));
            Assert.That(store.TryGet(ProxyFingerprint.FromFile(readyPath), ProxyPreset.Quarter), Is.Null);
            Assert.That(File.Exists(readyPath), Is.True);

            int confirmations = 0;
            model.ConfirmDeleteAllForProjectAsync = _ => { confirmations++; return Task.FromResult(false); };
            int beforeDeleteAll = store.Enumerate().Count();
            Click(more);
            Click(actions.Single(x => ReferenceEquals(x.Command, model.DeleteAllForProjectCommand)));
            Assert.That(confirmations, Is.EqualTo(1));
            Assert.That(store.Enumerate().Count(), Is.EqualTo(beforeDeleteAll));

            Click(Part<CheckBox>(FindRow(FindClip()), "SelectClipCheckBox"));
            Assert.That(model.HasSelection.Value, Is.False);
            Assert.That(generateSelected.Content, Is.EqualTo(Strings.ProxyGenerateAll));
            Assert.That(generateSelected.Command, Is.SameAs(model.GenerateAllCommand));
            int enqueuedBeforeAll = queue.Enqueued.Count;
            Click(generateSelected);
            Assert.That(queue.Enqueued.Skip(enqueuedBeforeAll).Select(x => Path.GetFileName(x.Source.AbsolutePath)),
                Is.EquivalentTo(new[] { "CITY.MOV", "FAILED.MOV" }).IgnoreCase,
                "The existing bulk action targets the remaining eligible sources, independently of selection.");
            Assert.That(queue.Enqueued.Skip(enqueuedBeforeAll).All(x => x.Priority == 0), Is.True);

            Click(Part<CheckBox>(FindRow(FindClip()), "SelectClipCheckBox"));
            ProxyClipViewModel interview = model.Clips.Single(x => x.FileName == "interview.mov");
            Click(Part<CheckBox>(FindRow(interview), "SelectClipCheckBox"));
            Assert.That(generateSelected.Content, Is.EqualTo(Strings.ProxyGenerateSelected));
            int enqueuedBeforeSelection = queue.Enqueued.Count;
            Click(generateSelected);
            Assert.That(queue.Enqueued.Skip(enqueuedBeforeSelection).Select(x => x.Source),
                Is.EquivalentTo(new[] { FindClip().Source, interview.Source }));
            Assert.That(queue.Enqueued.Skip(enqueuedBeforeSelection).All(x => x.Priority == 1), Is.True);

            window.Width = 260;
            window.Height = 180;
            HeadlessTestHelpers.Render(3);
            CheckHorizontalBounds();
            row = FindRow(FindClip());
            Button rowMore = Part<Button>(row, "ClipMoreButton");
            rowMore.Focus(NavigationMethod.Tab);
            HeadlessTestHelpers.Render();
            Point focused = rowMore.TranslatePoint(default, window)!.Value;
            Assert.That(focused.Y, Is.GreaterThanOrEqualTo(38));
            Assert.That(focused.Y + rowMore.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));

            scene.Children.Clear();
            HeadlessTestHelpers.Render(3);
            Assert.That(model.HasClips.Value, Is.False);
            Assert.That(model.HasSelection.Value, Is.False);
            Assert.That(view.FindControl<Border>("EmptyState")!.IsEffectivelyVisible, Is.True);

            ProxyClipViewModel FindClip() => model.Clips.Single(x => x.Path == readyPath);

            Border FindRow(ProxyClipViewModel item) => view.GetVisualDescendants().OfType<Border>()
                .Single(x => x.Classes.Contains("proxyItem") && ReferenceEquals(x.DataContext, item));

            void CheckHorizontalBounds()
            {
                foreach (Border item in view.GetVisualDescendants().OfType<Border>().Where(x => x.Classes.Contains("proxyItem")))
                {
                    Grid actions = Part<Grid>(item, "ClipActions");
                    Button more = Part<Button>(item, "ClipMoreButton");
                    Button primary = Part<Button>(item, "GenerateClipButton").IsVisible
                        ? Part<Button>(item, "GenerateClipButton") : Part<Button>(item, "CancelJobButton");
                    Assert.That(more.Bounds.Right, Is.EqualTo(actions.Bounds.Width).Within(0.001));
                    Assert.That(primary.Bounds.Right, Is.LessThanOrEqualTo(more.Bounds.Left));
                    Assert.That(Part<ComboBox>(item, "QualityComboBox").Bounds.Right,
                        Is.LessThanOrEqualTo(primary.Bounds.Left));
                    foreach (Control action in item.GetVisualDescendants().OfType<Control>()
                                 .Where(x => x.IsEffectivelyVisible && (x is Button || x is ComboBox || x is CheckBox)))
                    {
                        Point point = action.TranslatePoint(default, item)!.Value;
                        Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                        Assert.That(point.X + action.Bounds.Width, Is.LessThanOrEqualTo(item.Bounds.Width + 0.001));
                    }
                }
            }
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }

        string AddSource(string name, ProxyState? state)
        {
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, new byte[4096]);
            var source = new VideoSource();
            source.ReadFrom(new Uri(path));
            var video = new SourceVideo();
            video.Source.CurrentValue = source;
            var element = new Element { Uri = new Uri(Path.Combine(root, name + ".layer")), Length = TimeSpan.FromSeconds(1) };
            element.AddObject(video);
            scene.Children.Add(element);
            if (state is { } value)
            {
                string relative = name + ".proxy.mp4";
                Directory.CreateDirectory(store.StoreRootPath);
                File.WriteAllBytes(Path.Combine(store.StoreRootPath, relative), new byte[2048]);
                store.Register(new ProxyEntry(ProxyFingerprint.FromFile(path), ProxyPreset.Quarter, value, relative,
                    2048, new PixelSize(1920, 1080), new PixelSize(480, 270), DateTime.UtcNow, DateTime.UtcNow,
                    value == ProxyState.Failed ? "The media could not be decoded. Try generating the proxy again." : null));
            }
            return path;
        }

        void Click(Control control)
        {
            control.BringIntoView();
            HeadlessTestHelpers.Render(3);
            window.UpdateLayout();
            TopLevel top = TopLevel.GetTopLevel(control)!;
            Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
            top.MouseMove(point);
            top.MouseDown(point, MouseButton.Left);
            top.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_PROXY_DESIGN_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private static T Part<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(x => x.Name == name);

    private sealed class TestQueue : IProxyJobQueue
    {
        private readonly List<ProxyJob> _pending = [];
        public List<ProxyJob> Enqueued { get; } = [];
        public List<Guid> Canceled { get; } = [];
        public int MaxConcurrency => 1;
        public event EventHandler<ProxyJobChangedEventArgs>? JobChanged;
        public IReadOnlyList<ProxyJob> Pending() => _pending.ToArray();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void CancelAll() { foreach (ProxyJob job in _pending.ToArray()) Cancel(job.JobId); }
        public ValueTask<ProxyJob> EnqueueAsync(ProxyFingerprint source, ProxyPreset preset, int priority = 0, CancellationToken cancellationToken = default)
        {
            var job = new ProxyJob(source, preset, priority: priority);
            _pending.Add(job);
            Enqueued.Add(job);
            JobChanged?.Invoke(this, new ProxyJobChangedEventArgs { Job = job, Kind = ProxyJobChangeKind.Enqueued });
            return ValueTask.FromResult(job);
        }
        public void Cancel(Guid id)
        {
            Canceled.Add(id);
            if (_pending.FirstOrDefault(x => x.JobId == id) is not { } job) return;
            _pending.Remove(job);
            SetState(job, ProxyJobStatus.Canceled);
            JobChanged?.Invoke(this, new ProxyJobChangedEventArgs { Job = job, Kind = ProxyJobChangeKind.Canceled });
        }
        public void ReportProgress(ProxyJob job, double progress)
        {
            SetState(job, ProxyJobStatus.Running);
            typeof(ProxyJob).GetProperty(nameof(ProxyJob.LatestProgress))!.SetValue(job, new ProxyJobProgress(progress, TimeSpan.FromSeconds(18)));
            JobChanged?.Invoke(this, new ProxyJobChangedEventArgs { Job = job, Kind = ProxyJobChangeKind.Progressed });
        }
        // The real queue owns these engine-internal setters; the test queue drives the same events.
        private static void SetState(ProxyJob job, ProxyJobStatus state) => typeof(ProxyJob).GetProperty(nameof(ProxyJob.Status))!.SetValue(job, state);
    }
}
