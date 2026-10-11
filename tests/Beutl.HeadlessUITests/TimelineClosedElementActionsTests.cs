using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Element actions that used to go through the timeline tab and did nothing once it was closed.
[TestFixture]
[NonParallelizable]
public class TimelineClosedElementActionsTests
{
    private const string TestVideoExtension = ".timeline-closed-video";

    [AvaloniaTest]
    public async Task Menu_paste_adds_the_copied_element_at_the_playhead_with_the_timeline_closed()
    {
        EditViewModel editor = await OpenEditorForNewScene("timeline-closed-paste");
        Element copied = await AddElementAsync(editor, () => new RectShape(), lengthSeconds: 4);
        CloseTimeline(editor);
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = copied;
        editor.GetRequiredService<IEditorClock>().CurrentTime.Value = TimeSpan.FromSeconds(1);
        MenuBarViewModel menuBar = TestShell.MainViewModel.MenuBar;
        var window = new Window();
        using var notifications = new NotificationCapture();
        try
        {
            window.Show();
            ClipboardHelper.OverrideClipboard = window.Clipboard;

            await menuBar.CopyLayer.ExecuteAsync();
            await menuBar.PasteLayer.ExecuteAsync();
            HeadlessTestHelpers.Settle();

            Assert.That(editor.Scene.Children, Has.Count.EqualTo(2));
            Element pasted = editor.Scene.Children.Single(e => e != copied);
            Assert.Multiple(() =>
            {
                Assert.That(pasted.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(pasted.Length, Is.EqualTo(copied.Length));
                // The layer above the copied clip, which shows at the playhead.
                Assert.That(pasted.ZIndex, Is.EqualTo(1));
                Assert.That(pasted.Objects.OfType<RectShape>().Count(), Is.EqualTo(1));
                Assert.That(notifications.Notifications, Is.Empty);
            });
        }
        finally
        {
            ClipboardHelper.OverrideClipboard = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Choosing_a_new_video_matches_the_clip_length_with_the_timeline_closed()
    {
        EditViewModel editor = await OpenEditorForNewScene("timeline-closed-media-length");
        Element element = await AddElementAsync(editor, () => new SourceVideo(), lengthSeconds: 5);
        var video = element.Objects.OfType<SourceVideo>().Single();
        CloseTimeline(editor);
        var adapter = new AnimatablePropertyAdapter<VideoSource?>((AnimatableProperty<VideoSource?>)video.Source, video);
        using var viewModel = new VideoSourceEditorViewModel(adapter);
        viewModel.Accept(new EditorServices(editor, element));
        var decoder = new TestVideoDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            // What the video source editor does once a file is picked. The test video is 37 frames at 25 fps
            // (1.48 s), which is not a whole number of frames at the project's 30 fps.
            viewModel.SetValue(VideoSource.Open(CreateTestVideoFile()));
            MediaSourceEditorHelper.MatchElementToOriginalDuration(viewModel);

            Assert.Multiple(() =>
            {
                Assert.That(video.Source.CurrentValue, Is.Not.Null);
                Assert.That(element.Start, Is.EqualTo(TimeSpan.Zero));
                Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(37d / 25).FloorToRate(30)));
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    [AvaloniaTest]
    public async Task Edit_animation_in_inline_view_opens_the_closed_timeline_first()
    {
        EditViewModel editor = await OpenEditorForNewScene("timeline-closed-inline-animation");
        Element element = await AddElementAsync(editor, () => new RectShape(), lengthSeconds: 4);
        var shape = element.Objects.OfType<RectShape>().Single();
        CloseTimeline(editor);
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = element;
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        var menu = new PropertyEditorMenu { DataContext = WidthEditor(tab, shape) };
        var window = new Window { Width = 320, Height = 240, Content = menu };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            FAMenuFlyoutItem item = ((FAMenuFlyout)button.ContextFlyout!).Items.OfType<FAMenuFlyoutItem>()
                .Single(i => i.Name == "editInlineAnimationItem");

            item.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            HeadlessTestHelpers.Settle();

            TimelineTabViewModel? timeline = editor.FindToolTab<TimelineTabViewModel>();
            Assert.That(timeline, Is.Not.Null, "The inline view lives in the timeline, so the timeline opens.");
            InlineAnimationLayerViewModel inline = timeline!.Inlines.Single();
            Assert.Multiple(() =>
            {
                Assert.That(inline.Element.Model, Is.SameAs(element));
                Assert.That(inline.Property.GetEngineProperty(), Is.SameAs(shape.Width));
                Assert.That(shape.Width.Animation, Is.InstanceOf<IKeyFrameAnimation>());
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        await TestReset.ResetShellAsync();
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, location))!;
        HeadlessTestHelpers.Settle();
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

    private static async Task<Element> AddElementAsync(EditViewModel editor, Func<EngineObject> create, double lengthSeconds)
    {
        ElementAddResult result = await editor.GetRequiredService<IElementAdder>().AddAsync(
            [new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(lengthSeconds), 0, new ElementSource.EngineObject(create))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        Assert.That(result.IsSuccess, Is.True);
        return result.Items.Single().PrimaryElement;
    }

    private static void CloseTimeline(EditViewModel editor)
    {
        editor.CloseToolTab(editor.FindToolTab<TimelineTabViewModel>()!);
        HeadlessTestHelpers.Settle();
        Assert.That(editor.FindToolTab<TimelineTabViewModel>(), Is.Null);
    }

    private static string CreateTestVideoFile()
    {
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"replacement-{Guid.NewGuid():N}{TestVideoExtension}");
        File.WriteAllBytes(path, []);
        return path;
    }

    private static NumberEditorViewModel<float> WidthEditor(ElementPropertyTabViewModel tab, RectShape shape)
        => Editors(tab.Items[0].Properties).OfType<NumberEditorViewModel<float>>()
            .Single(x => ReferenceEquals(x.PropertyAdapter.GetEngineProperty(), shape.Width));

    private static IEnumerable<IPropertyEditorContext> Editors(IReadOnlyList<IPropertyEditorContext?> properties)
    {
        foreach (IPropertyEditorContext? property in properties)
        {
            if (property is PropertyEditorGroupContext group)
            {
                foreach (IPropertyEditorContext child in Editors(group.Properties))
                    yield return child;
            }
            else if (property != null)
            {
                yield return property;
            }
        }
    }

    // The services a property editor gets from the property tab it is shown in.
    private sealed class EditorServices(EditViewModel editor, Element element) : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(EditViewModel)) return editor;
            if (serviceType == typeof(Element)) return element;
            return null;
        }
    }

    private sealed class NotificationCapture : INotificationServiceHandler, IDisposable
    {
        private readonly INotificationServiceHandler _previous = NotificationService.Handler;

        public NotificationCapture() => NotificationService.Handler = this;

        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);

        public void Dispose() => NotificationService.Handler = _previous;
    }

    private sealed class TestVideoDecoder : IDecoderInfo
    {
        public string Name => "Timeline-closed test decoder";
        public IEnumerable<string> VideoExtensions() => [TestVideoExtension];
        public IEnumerable<string> AudioExtensions() => [];
        public MediaReader? Open(string file, MediaOptions options)
            => Path.GetExtension(file) == TestVideoExtension ? new TestVideoReader() : null;
    }

    private sealed class TestVideoReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("test", 37, new PixelSize(16, 16), new Rational(25, 1));
        public override AudioStreamInfo AudioInfo => throw new NotSupportedException();
        public override bool HasVideo => true;
        public override bool HasAudio => false;
        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image) { image = null; return false; }
        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound) { sound = null; return false; }
    }
}
