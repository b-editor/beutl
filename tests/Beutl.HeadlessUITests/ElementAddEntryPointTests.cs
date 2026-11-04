using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.TimelineTab;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using AvaPoint = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class ElementAddEntryPointTests
{
    private static async Task<(EditViewModel Editor, TimelineTabViewModel Timeline)> OpenEditorForNewScene(
        string name)
    {
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()
                                            ?? throw new InvalidOperationException(
                                                "The default editor layout did not create a timeline tab.");
        return (editor, timeline);
    }

    [AvaloniaTest]
    public async Task TimelineAddElement_Success_AddsAndScrollsWithoutNotification()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-add-entry-success");
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var description = new ElementDescription(
            MediaTime.FromSeconds(3),
            MediaTime.FromSeconds(2),
            4,
            new ElementSource.EngineObject(() => new RectShape()));

        await timeline.AddElement.ExecuteAsync(description);
        HeadlessTestHelpers.Settle();

        Element created = editor.Scene.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Start.Value, Is.EqualTo(description.Start));
            Assert.That(created.Length.Value, Is.EqualTo(description.Length));
            Assert.That(created.ZIndex.Value, Is.EqualTo(description.Layer));
            Assert.That(created.Objects.OfType<RectShape>().Count(), Is.EqualTo(1));
            Assert.That(scrolls, Has.Count.EqualTo(1));
            Assert.That(scrolls[0].Range, Is.EqualTo(created.Range));
            Assert.That(scrolls[0].ZIndex, Is.EqualTo(created.ZIndex.Value));
            Assert.That(notifications.All, Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task TimelineAddElement_LockedLayer_ShowsWarningWithoutAddingOrScrolling()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-add-entry-locked");
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });
        }

        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);

        await timeline.AddElement.ExecuteAsync(new ElementDescription(
            MediaTime.Zero,
            MediaTime.FromSeconds(2),
            2,
            new ElementSource.EngineObject(() => new RectShape())));
        HeadlessTestHelpers.Settle();

        Notification notification = notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(scrolls, Is.Empty);
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notification.Title, Is.EqualTo(Strings.Lock));
            Assert.That(notification.Message, Is.EqualTo(Strings.LayerIsLocked));
        }
    }

    [Test]
    public void A_refused_paste_says_why()
    {
        using var notifications = new NotificationCapture();

        TimelineTabViewModel.ShowPasteFailure(new LockedElementLayerFailure(1));
        TimelineTabViewModel.ShowPasteFailure(new ElementSceneNotSavedFailure());
        TimelineTabViewModel.ShowPasteFailure(new InvalidElementMaterializationFailure("failed"));

        Assert.That(
            notifications.All.Select(notification => (notification.Type, notification.Title, notification.Message)),
            Is.EqualTo(new[]
            {
                (NotificationType.Warning, Strings.Lock, Strings.LayerIsLocked),
                (NotificationType.Warning, Strings.Paste, Strings.Paste_ProjectNotSaved),
                (NotificationType.Error, Strings.AddElement, MessageStrings.UnexpectedError),
            }));
    }

    [AvaloniaTest]
    public async Task TimelineAddElement_GeneralFailure_ShowsErrorWithoutAddingOrScrolling()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-add-entry-failure");
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);

        await timeline.AddElement.ExecuteAsync(new ElementDescription(
            MediaTime.Zero,
            MediaTime.FromSeconds(2),
            0,
            new ElementSource.EngineObject(
                static () => throw new InvalidOperationException("Injected element factory failure."))));
        HeadlessTestHelpers.Settle();

        Notification notification = notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(editor.HistoryManager.UndoCount, Is.Zero);
            Assert.That(scrolls, Is.Empty);
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
            Assert.That(notification.Title, Is.EqualTo(Strings.AddElement));
            Assert.That(notification.Message, Is.EqualTo(MessageStrings.UnexpectedError));
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task TimelineView_TemplateFileDrop_AddsCompleteTemplateAndScrollsToDropTarget(bool fromStorage)
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-template-drop");
        string templatePath = CreateElementTemplateFile(editor, "dropped-template.json");
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel timelinePanel = view.FindControl<Panel>("TimelinePanel")!;
            Assert.That(timelinePanel, Is.Not.Null);
            using IStorageFile storageFile = await GetStorageFile(window, templatePath);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(storageFile));
            var dropPoint = new AvaPoint(180, timeline.CalculateLayerTop(3) + 5);
            if (fromStorage)
                transfer.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("test", new object(), [], [templatePath], () => true, _ => Task.FromResult(false))));
            int expectedLayer = timeline.ToLayerNumber(dropPoint.Y);
            var args = new DragEventArgs(
                DragDrop.DropEvent,
                transfer,
                timelinePanel,
                dropPoint,
                KeyModifiers.None);
            Element created = await RaiseDropAndWaitForElement(editor.Scene, timelinePanel, args);
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(created.Start.Value, Is.EqualTo(timeline.ClickedFrame));
                Assert.That(created.Length.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
                Assert.That(created.ZIndex.Value, Is.EqualTo(expectedLayer));
                Assert.That(created.Name.Value, Is.EqualTo("Dropped template"));
                Assert.That(created.Objects.OfType<RectShape>().Count(), Is.EqualTo(1));
                Assert.That(scrolls, Has.Count.EqualTo(1));
                Assert.That(scrolls[0].Range, Is.EqualTo(created.Range));
                Assert.That(scrolls[0].ZIndex, Is.EqualTo(created.ZIndex.Value));
                Assert.That(notifications.All, Is.Empty);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_SeveralFilesDrop_LinesThemUpInNameOrderInOneUndoStep()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-several-files-drop");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel timelinePanel = view.FindControl<Panel>("TimelinePanel")!;
            string directory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
            string notes = Path.Combine(directory, "notes.beutl-unknown");
            File.WriteAllText(notes, "not media");
            // The shorter sound follows the longer one rather than fitting under it on another layer.
            string longSound = Path.Combine(directory, "a.wav");
            WriteSilentWave(longSound, 13230);
            string shortSound = Path.Combine(directory, "b.wav");
            WriteSilentWave(shortSound, 4410);
            using IStorageFile ten = await GetStorageFile(window, CreatePngFile(editor, "10.png"));
            using IStorageFile one = await GetStorageFile(window, CreatePngFile(editor, "1.png"));
            using IStorageFile two = await GetStorageFile(window, CreatePngFile(editor, "2.png"));
            using IStorageFile notesFile = await GetStorageFile(window, notes);
            using IStorageFile longFile = await GetStorageFile(window, longSound);
            using IStorageFile shortFile = await GetStorageFile(window, shortSound);
            using var transfer = new DataTransfer();
            foreach (IStorageFile file in new[] { ten, notesFile, shortFile, longFile, one, two })
                transfer.Add(DataTransferItem.CreateFile(file));
            var dropPoint = new AvaPoint(180, timeline.CalculateLayerTop(3) + 5);
            int layer = timeline.ToLayerNumber(dropPoint.Y);
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;
            var args = new DragEventArgs(DragDrop.DropEvent, transfer, timelinePanel, dropPoint, KeyModifiers.None);

            await RaiseDropAndWaitForElement(editor.Scene, timelinePanel, args);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.UndoCount > undoCount);
            HeadlessTestHelpers.Settle();

            MediaTime frame = timeline.ClickedFrame;
            MediaTime length = MediaTime.FromSeconds(5);
            Element[] pictures = [.. editor.Scene.Children.Where(child => child.Objects.OfType<SourceImage>().Any())
                .OrderBy(child => child.Start.Value)];
            Element[] sounds = [.. editor.Scene.Children.Where(child => child.Objects.OfType<SourceSound>().Any())
                .OrderBy(child => child.Start.Value)];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(editor.Scene.Children, Has.Count.EqualTo(5));
                Assert.That(pictures.Select(p => Path.GetFileName(p.Objects.OfType<SourceImage>().Single().Source.Value!.Uri.LocalPath)),
                    Is.EqualTo(new[] { "1.png", "2.png", "10.png" }));
                Assert.That(pictures.Select(p => p.Start.Value),
                    Is.EqualTo(new[] { frame, frame + length, frame + length + length }));
                Assert.That(pictures.Select(p => p.ZIndex.Value), Is.All.EqualTo(layer));
                Assert.That(sounds.Select(s => Path.GetFileName(s.Objects.OfType<SourceSound>().Single().Source.Value!.Uri.LocalPath)),
                    Is.EqualTo(new[] { "a.wav", "b.wav" }));
                Assert.That(sounds.Select(s => s.Start.Value), Is.EqualTo(new[] { frame, sounds[0].Range.End }));
                Assert.That(sounds.Select(s => s.ZIndex.Value), Is.All.EqualTo(layer + 1));
                Assert.That(scrolls.Single().Range, Is.EqualTo(pictures[0].Range));
                Assert.That(notifications.All, Is.Empty);
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
            }

            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(editor.Scene.Children, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            DecoderRegistry.Unregister(decoder);
        }
    }

    // Background music in a .webm or .mp4 runs under the pictures like any other sound.
    [AvaloniaTest]
    public async Task TimelineView_SeveralFilesDrop_TreatsAContainerHoldingOnlySoundAsSound()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-sound-in-video-container-drop");
        var decoder = new SoundInVideoContainerDecoder();
        DecoderRegistry.Register(decoder);
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel timelinePanel = view.FindControl<Panel>("TimelinePanel")!;
            string music = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "2.wav");
            WriteSilentWave(music, 4410);
            using IStorageFile first = await GetStorageFile(window, CreatePngFile(editor, "1.png"));
            using IStorageFile second = await GetStorageFile(window, music);
            using IStorageFile third = await GetStorageFile(window, CreatePngFile(editor, "3.png"));
            using var transfer = new DataTransfer();
            foreach (IStorageFile file in new[] { first, second, third })
                transfer.Add(DataTransferItem.CreateFile(file));
            var dropPoint = new AvaPoint(180, timeline.CalculateLayerTop(3) + 5);
            int layer = timeline.ToLayerNumber(dropPoint.Y);
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;
            var args = new DragEventArgs(DragDrop.DropEvent, transfer, timelinePanel, dropPoint, KeyModifiers.None);

            await RaiseDropAndWaitForElement(editor.Scene, timelinePanel, args);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.UndoCount > undoCount);
            HeadlessTestHelpers.Settle();

            MediaTime frame = timeline.ClickedFrame;
            Element[] pictures = [.. editor.Scene.Children.Where(child => child.Objects.OfType<SourceImage>().Any())
                .OrderBy(child => child.Start.Value)];
            Element sound = editor.Scene.Children.Single(child => child.Objects.OfType<SourceSound>().Any());
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pictures.Select(p => p.Start.Value),
                    Is.EqualTo(new[] { frame, frame + MediaTime.FromSeconds(5) }));
                Assert.That(pictures.Select(p => p.ZIndex.Value), Is.All.EqualTo(layer));
                Assert.That(sound.Start.Value, Is.EqualTo(frame));
                Assert.That(sound.ZIndex.Value, Is.EqualTo(layer + 1));
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            DecoderRegistry.Unregister(decoder);
        }
    }

    // Lists sound files as videos as well, which is what FFmpeg does with the containers that can hold either.
    private sealed class SoundInVideoContainerDecoder : IDecoderInfo
    {
        private readonly WaveDecoderInfo _wave = new();

        public string Name => "Sound in a video container";

        public MediaReader? Open(string file, MediaOptions options) => _wave.Open(file, options);

        public IEnumerable<string> VideoExtensions() => _wave.AudioExtensions();

        public IEnumerable<string> AudioExtensions() => _wave.AudioExtensions();
    }

    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileDrop_AddsOneTextClipPerCueAboveTheScene()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-drop");
        using var notifications = new NotificationCapture();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel timelinePanel = view.FindControl<Panel>("TimelinePanel")!;
            string directory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
            await timeline.AddElementWithResultAsync(new ElementDescription(
                MediaTime.Zero, MediaTime.FromSeconds(1), 2, new ElementSource.EngineObject(() => new RectShape())));
            HeadlessTestHelpers.Settle();
            string path = Path.Combine(directory, "movie.srt");
            File.WriteAllText(path, "1\n00:00:01,000 --> 00:00:02,500\nHello\n\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n");
            using IStorageFile file = await GetStorageFile(window, path);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(file));
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;
            var args = new DragEventArgs(
                DragDrop.DropEvent, transfer, timelinePanel, new AvaPoint(500, 5), KeyModifiers.None);

            timelinePanel.RaiseEvent(args);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.UndoCount > undoCount);
            HeadlessTestHelpers.Settle();
            HeadlessCapture.SaveIfRequested(window, nameof(ElementAddEntryPointTests), "subtitle-drop");

            Element[] captions = [.. editor.Scene.Children.Where(child => child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Any())
                .OrderBy(child => child.Start.Value)];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(captions.Select(c => c.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Single().Text.Value),
                    Is.EqualTo(new[] { "Hello", "World" }));
                Assert.That(captions.Select(c => c.Range), Is.EqualTo(new[]
                {
                    new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1.5)),
                    new TimeRange(MediaTime.FromSeconds(3), MediaTime.FromSeconds(1)),
                }));
                Assert.That(captions.Select(c => c.ZIndex.Value), Is.All.EqualTo(3));
                Assert.That(captions.Select(c => c.Name.Value), Is.All.EqualTo("movie"));
                Assert.That(notifications.All, Is.Empty);
            }

            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(editor.Scene.Children.Where(child => child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Any()), Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_SubtitleFile_KeepsTheTimesOfCuesThatOverlap()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-overlap");
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "overlap.srt");
        File.WriteAllText(path, """
            1
            00:00:01,000 --> 00:00:03,000
            first

            2
            00:00:02,000 --> 00:00:04,000
            second

            3
            00:00:03,500 --> 00:00:05,000
            third

            """);

        await CaptionFileDrop.AddAsync(timeline, path);

        Element[] clips = [.. editor.Scene.Children.OrderBy(child => child.Start.Value)];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clips.Select(clip => clip.Range), Is.EqualTo(new[]
            {
                new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2)),
                new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)),
                new TimeRange(MediaTime.FromSeconds(3.5), MediaTime.FromSeconds(1.5)),
            }));
            Assert.That(clips.Select(clip => clip.ZIndex.Value), Is.EqualTo(new[] { 0, 1, 0 }),
                "The third starts after the first ends, so it can share its layer.");
        }
    }

    // The clips in the scene push the dropped picture off its layer, and it must not land where the subtitles go.
    [AvaloniaTest]
    public async Task TimelineView_SubtitleFile_KeepsItsTimesWhenWhatIsDroppedWithItIsPushedUp()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-displaced");
        for (int layer = 0; layer < 3; layer++)
        {
            await timeline.AddElementWithResultAsync(new ElementDescription(
                MediaTime.Zero, MediaTime.FromSeconds(10), layer, new ElementSource.EngineObject(() => new RectShape())));
        }

        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "pushed.srt");
        File.WriteAllText(path, "1\n00:00:05,000 --> 00:00:08,000\nsubtitle\n");
        var picture = new ElementDescription(
            MediaTime.FromSeconds(1), MediaTime.FromSeconds(5), 0, new ElementSource.EngineObject(() => new RectShape()));

        await CaptionFileDrop.AddAsync(timeline, [path], [picture]);

        Element text = editor.Scene.Children.Single(child => child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Any());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Range, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(3))));
            Assert.That(text.ZIndex.Value, Is.GreaterThan(editor.Scene.Children.Where(child => child != text).Max(child => child.ZIndex.Value)),
                "Subtitles stay above the picture they were dropped with.");
        }
    }

    // One unreadable file is said to be so, and need not cost the user the others.
    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileThatCannotBeRead_LeavesTheRestOfTheDrop()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-mixed");
        using var notifications = new NotificationCapture();
        string directory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
        string broken = Path.Combine(directory, "broken.srt");
        string missing = Path.Combine(directory, "missing.srt");
        string good = Path.Combine(directory, "good.srt");
        File.WriteAllText(broken, "this is not a subtitle file");
        File.WriteAllText(good, "1\n00:00:01,000 --> 00:00:02,000\ngood\n");

        await CaptionFileDrop.AddAsync(timeline, [broken, missing, good], []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children.Single().Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Single().Text.Value, Is.EqualTo("good"));
            Assert.That(notifications.All.Select(item => item.Message), Has.Exactly(1).Contain("broken.srt"));
            Assert.That(notifications.All.Select(item => item.Message), Has.Exactly(1).Contain("missing.srt"));
        }
    }

    // The picture is where it was dropped, and the file decides where its subtitles go.
    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileDroppedWithAPicture_ShowsTheFirstSubtitle()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-shown");
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "late.srt");
        File.WriteAllText(path, "1\n00:00:30,000 --> 00:00:31,000\nlate\n");
        var picture = new ElementDescription(
            MediaTime.Zero, MediaTime.FromSeconds(5), 0, new ElementSource.EngineObject(() => new RectShape()));
        (TimeRange Range, int ZIndex)? shown = null;
        using IDisposable subscription = timeline.ScrollTo.Subscribe(value => shown = value);

        await CaptionFileDrop.AddAsync(timeline, [path], [picture]);

        Element text = editor.Scene.Children.Single(child => child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Any());
        Assert.That(shown, Is.EqualTo((text.Range, text.ZIndex.Value)));
    }

    [AvaloniaTest]
    public async Task TimelineView_DroppingSeveralFiles_TakesThemAllBackInOneUndo()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-undo");
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel timelinePanel = view.FindControl<Panel>("TimelinePanel")!;
            string directory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
            string first = Path.Combine(directory, "first.srt");
            string second = Path.Combine(directory, "second.srt");
            File.WriteAllText(first, "1\n00:00:01,000 --> 00:00:02,000\nfirst\n");
            File.WriteAllText(second, "1\n00:00:01,000 --> 00:00:02,000\nsecond\n");
            string sound = Path.Combine(directory, "sound.wav");
            WriteSilentWave(sound, 48000);
            using IStorageFile firstFile = await GetStorageFile(window, first);
            using IStorageFile secondFile = await GetStorageFile(window, second);
            using IStorageFile soundFile = await GetStorageFile(window, sound);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(firstFile));
            transfer.Add(DataTransferItem.CreateFile(secondFile));
            transfer.Add(DataTransferItem.CreateFile(soundFile));
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;

            timelinePanel.RaiseEvent(new DragEventArgs(
                DragDrop.DropEvent, transfer, timelinePanel, new AvaPoint(500, 5), KeyModifiers.None));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.UndoCount > undoCount);
            HeadlessTestHelpers.Settle();

            Element[] texts = [.. editor.Scene.Children.Where(child => child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Any())];
            Element[] sounds = [.. editor.Scene.Children.Where(child => !child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>().Any())];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
                Assert.That(texts, Has.Length.EqualTo(2));
                Assert.That(sounds, Is.Not.Empty);
                Assert.That(texts.Select(text => text.ZIndex.Value).Distinct().Count(), Is.EqualTo(2));
                Assert.That(texts.Min(text => text.ZIndex.Value), Is.GreaterThan(sounds.Max(item => item.ZIndex.Value)));
            }

            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(editor.Scene.Children, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileWithoutCues_SaysSoAndAddsNothing()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-empty");
        using var notifications = new NotificationCapture();
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "broken.srt");
        File.WriteAllText(path, "this is not a subtitle file");

        await CaptionFileDrop.AddAsync(timeline, path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Scene.Children, Is.Empty);
            Assert.That(notifications.All.Single().Message, Does.Contain("broken.srt"));
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_SubtitleFile_KeepsShortCuesShowsTheFirstAndWarnsOfSkippedOnes()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-short");
        using var notifications = new NotificationCapture();
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "short.srt");
        File.WriteAllText(path, "1\n00:00:03,000 --> 00:00:03,200\nshort\n\n2\nnot a time\nskipped\n");
        (TimeRange Range, int ZIndex)? shown = null;
        using IDisposable subscription = timeline.ScrollTo.Subscribe(value => shown = value);

        await CaptionFileDrop.AddAsync(timeline, path);

        Element clip = editor.Scene.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clip.Start.Value.ToSeconds(), Is.EqualTo(3).Within(0.001));
            Assert.That(clip.Length.Value.ToSeconds(), Is.EqualTo(0.2).Within(0.001));
            Assert.That(shown, Is.EqualTo((clip.Range, clip.ZIndex.Value)));
            Assert.That(notifications.All.Single().Message, Does.Contain("short.srt"));
        }
    }

    [Test]
    public void Subtitle_files_are_read_in_UTF16_and_the_systems_legacy_code_page()
    {
        const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nこんにちは\n";
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Srt)];
            byte[] shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes(Srt);

            Assert.Multiple(() =>
            {
                Assert.That(CaptionFileDrop.Import(utf16, CaptionFormats.Srt).Document![0].Text, Is.EqualTo("こんにちは"));
                Assert.That(CaptionFileDrop.Import(shiftJis, CaptionFormats.Srt).Document![0].Text, Is.EqualTo("こんにちは"));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // One click on a template is all a drop needs, and the next drop offers that template under the pointer.
    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileDrop_OneClickOnATemplateAddsTheClipsAndItComesFirstNextTime()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-template");
        ObjectTemplateItem saved = await SaveTextTemplateAsync();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        bool overlayPopups = TestAppBuilder.HeadlessOptions.OverlayPopups;
        bool activated = false;

        try
        {
            // The desktop app opens a menu as a window of its own, which a small window does not push aside.
            TestAppBuilder.HeadlessOptions.OverlayPopups = false;
            window.Show();
            HeadlessTestHelpers.Render();
            string directory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
            string first = Path.Combine(directory, "first.srt");
            File.WriteAllText(first, "1\n00:00:01,000 --> 00:00:02,000\nHello\n\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n");
            string second = Path.Combine(directory, "second.srt");
            File.WriteAllText(second, "1\n00:00:06,000 --> 00:00:07,000\nAgain\n");
            string third = Path.Combine(directory, "third.srt");
            File.WriteAllText(third, "1\n00:00:09,000 --> 00:00:10,000\nOnce more\n");
            Beutl.Graphics.Shapes.TextBlock[] Texts() => [.. editor.Scene.Children
                .OrderBy(child => child.Start.Value)
                .SelectMany(child => child.Objects.OfType<Beutl.Graphics.Shapes.TextBlock>())];
            window.Activated += (_, _) => activated = true;

            await RaiseFileDropAsync(window, view, first);
            FAMenuFlyoutItem[] firstOffer = await WaitForTemplateMenuAsync(window);
            string?[] firstNames = [.. firstOffer.Select(item => item.Text)];
            HeadlessTestHelpers.Click(firstOffer.Single(item => item.Text == saved.Name.Value));
            await HeadlessTestHelpers.WaitUntilAsync(() => Texts().Length == 2);

            // The click lands where the file was dropped, without the pointer moving.
            AvaPoint point = await RaiseFileDropAsync(window, view, second);
            FAMenuFlyoutItem[] secondOffer = await WaitForTemplateMenuAsync(window);
            string?[] secondNames = [.. secondOffer.Select(item => item.Text)];
            HeadlessTestHelpers.Click(secondOffer[0], secondOffer[0].PointToClient(window.PointToScreen(point)));
            await HeadlessTestHelpers.WaitUntilAsync(() => Texts().Length == 3);

            await RaiseFileDropAsync(window, view, third);
            await WaitForTemplateMenuAsync(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await HeadlessTestHelpers.WaitUntilAsync(() => Texts().Length == 4);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Texts().Select(text => text.Text.Value), Is.EqualTo(new[] { "Hello", "World", "Again", "Once more" }));
                Assert.That(Texts().Select(text => text.Size.Value), Is.All.EqualTo(123f));
                Assert.That(firstNames, Is.EqualTo(new[] { Strings.AiSubtitle_Template, Strings.AiSubtitle_DefaultTemplate, saved.Name.Value }));
                Assert.That(secondNames, Is.EqualTo(new[] { Strings.AiSubtitle_Template, saved.Name.Value, Strings.AiSubtitle_DefaultTemplate }));
                Assert.That(firstOffer[0].IsEffectivelyEnabled, Is.False);
                Assert.That(window.OpenedPopups, Is.Empty);
                Assert.That(activated, Is.True);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlayPopups;
            RemoveTemplate(saved);
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileDrop_DismissingTheMenuAddsNothing()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-dismiss");
        ObjectTemplateItem saved = await SaveTextTemplateAsync();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        bool overlayPopups = TestAppBuilder.HeadlessOptions.OverlayPopups;

        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = false;
            window.Show();
            HeadlessTestHelpers.Render();
            string directory = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
            string captions = Path.Combine(directory, "movie.srt");
            File.WriteAllText(captions, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            string image = CreatePngFile(editor, "beside.png");

            await RaiseFileDropAsync(window, view, captions, image);
            await WaitForTemplateMenuAsync(window);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            // Long enough for a drop that carried on to reach the scene.
            for (int attempt = 0; attempt < 20 && editor.Scene.Children.Count == 0; attempt++)
            {
                await Task.Delay(50);
                HeadlessTestHelpers.Settle();
            }

            Assert.That(window.OpenedPopups, Is.Empty);
            Assert.That(editor.Scene.Children, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlayPopups;
            RemoveTemplate(saved);
        }
    }

    [AvaloniaTest]
    public async Task TimelineView_SubtitleFileDrop_AsksNothingWithOnlyTheDefaultLook()
    {
        await TestReset.ResetShellAsync();
        Assume.That(CaptionFileDrop.GetTemplates(), Has.Count.EqualTo(1));
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("timeline-subtitle-default");
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "movie.srt");
            File.WriteAllText(path, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

            await RaiseFileDropAsync(window, view, path);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Scene.Children.Count == 1);

            Assert.That(window.OpenedPopups, Is.Empty);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static async Task<ObjectTemplateItem> SaveTextTemplateAsync()
    {
        var text = new Beutl.Graphics.Shapes.TextBlock();
        text.Size.Value = 123f;
        ObjectTemplateItem? saved = await ObjectTemplateService.Instance.AddFromInstanceAsync(text, $"subtitle-look-{Guid.NewGuid():N}");
        Assert.That(saved, Is.Not.Null);
        return saved!;
    }

    private static void RemoveTemplate(ObjectTemplateItem template)
    {
        File.Delete(template.FilePath!);
        ObjectTemplateService.Instance.RefreshFromFileSystem();
    }

    // Drops at the middle of the timeline through the window, as the platform does.
    private static async Task<AvaPoint> RaiseFileDropAsync(Window window, TimelineTabView view, params string[] paths)
    {
        var transfer = new DataTransfer();
        foreach (string path in paths)
        {
            transfer.Add(DataTransferItem.CreateFile(await GetStorageFile(window, path)));
        }

        AvaPoint point = HeadlessTestHelpers.Center(view.FindControl<ScrollViewer>("ContentScroll")!, window);
        window.DragDrop(point, RawDragEventType.DragEnter, transfer, DragDropEffects.Copy);
        window.DragDrop(point, RawDragEventType.DragOver, transfer, DragDropEffects.Copy);
        window.DragDrop(point, RawDragEventType.Drop, transfer, DragDropEffects.Copy);
        return point;
    }

    private static async Task<FAMenuFlyoutItem[]> WaitForTemplateMenuAsync(Window window)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => window.OpenedPopups.Any(popup => popup.Child is FAMenuFlyoutPresenter));
        HeadlessTestHelpers.Render(3);
        Control presenter = window.OpenedPopups.Single(popup => popup.Child is FAMenuFlyoutPresenter).Child!;
        return [.. presenter.GetVisualDescendants().OfType<FAMenuFlyoutItem>()];
    }

    private static void WriteSilentWave(string path, int frames)
    {
        int bytes = frames * 2 * sizeof(short);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        writer.Write(44100);
        writer.Write(44100 * 2 * sizeof(short));
        writer.Write((ushort)(2 * sizeof(short)));
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        writer.Write(new byte[bytes]);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task PlayerView_ImageFileDrop_RoutesThroughTimelineAndScrollsToAddedElement(bool fromStorage)
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("player-file-drop");
        string imagePath = CreatePngFile(editor, "dropped-image.png");
        editor.Player.CurrentFrame.Value = MediaTime.FromSeconds(4);
        editor.Player.PreviewImage.Value = Ref<Bitmap>.Create(new Bitmap(
            editor.Scene.FrameSize.Value.Width,
            editor.Scene.FrameSize.Value.Height));
        using var notifications = new NotificationCapture();
        var scrolls = new List<(TimeRange Range, int ZIndex)>();
        using IDisposable subscription = timeline.ScrollTo.Subscribe(scrolls.Add);
        var view = new PlayerView { DataContext = editor.Player };
        view.image.Width = editor.Scene.FrameSize.Value.Width;
        view.image.Height = editor.Scene.FrameSize.Value.Height;
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel framePanel = view.FindControl<Panel>("framePanel")!;
            Assert.That(framePanel, Is.Not.Null);
            var previewSize = new Avalonia.Size(editor.Scene.FrameSize.Value.Width, editor.Scene.FrameSize.Value.Height);
            view.image.Measure(previewSize);
            view.image.Arrange(new Avalonia.Rect(0, 0, previewSize.Width, previewSize.Height));
            Assert.That(view.image.Bounds.Width, Is.GreaterThan(0));
            Assert.That(view.image.Bounds.Height, Is.GreaterThan(0));
            using IStorageFile storageFile = await GetStorageFile(window, imagePath);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(storageFile));
            var imageCenter = new AvaPoint(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2);
            if (fromStorage)
                transfer.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("test", new object(), [], [imagePath], () => true, _ => Task.FromResult(false))));
            var args = new DragEventArgs(
                DragDrop.DropEvent,
                transfer,
                view.image,
                imageCenter,
                KeyModifiers.None);
            Element created = await RaiseDropAndWaitForElement(editor.Scene, framePanel, args);
            if (fromStorage)
                Assert.That(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "resources", "storage"), "*.png", SearchOption.AllDirectories), Has.Length.EqualTo(1));
            HeadlessTestHelpers.Settle();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(created.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
                Assert.That(created.Length.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
                Assert.That(created.ZIndex.Value, Is.Zero);
                Assert.That(created.Objects.OfType<SourceImage>().Count(), Is.EqualTo(1));
                Assert.That(scrolls, Has.Count.EqualTo(1));
                Assert.That(scrolls[0].Range, Is.EqualTo(created.Range));
                Assert.That(scrolls[0].ZIndex, Is.EqualTo(created.ZIndex.Value));
                Assert.That(notifications.All, Is.Empty);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task PlayerView_DropWithoutTimelineSurfacesLockedAndUnsupportedFailures()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) =
            await OpenEditorForNewScene("player-drop-without-timeline");
        editor.CloseToolTab(timeline);
        Assert.That(editor.FindToolTab<TimelineTabViewModel>(), Is.Null);
        using (editor.HistoryManager.SuppressRecording())
        {
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsLocked = { Value = true } });
        }
        string imagePath = CreatePngFile(editor, "locked-image.png");
        string unsupportedPath = Path.Combine(
            Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!,
            "unsupported.drop-test");
        await File.WriteAllTextAsync(unsupportedPath, "unsupported");
        editor.Player.CurrentFrame.Value = MediaTime.Zero;
        editor.Player.PreviewImage.Value = Ref<Bitmap>.Create(new Bitmap(
            editor.Scene.FrameSize.Value.Width,
            editor.Scene.FrameSize.Value.Height));
        using var notifications = new NotificationCapture();
        var view = new PlayerView { DataContext = editor.Player };
        view.image.Width = editor.Scene.FrameSize.Value.Width;
        view.image.Height = editor.Scene.FrameSize.Value.Height;
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel framePanel = view.FindControl<Panel>("framePanel")!;
            var previewSize = new Avalonia.Size(editor.Scene.FrameSize.Value.Width, editor.Scene.FrameSize.Value.Height);
            view.image.Measure(previewSize);
            view.image.Arrange(new Avalonia.Rect(0, 0, previewSize.Width, previewSize.Height));
            var imageCenter = new AvaPoint(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2);

            using IStorageFile lockedFile = await GetStorageFile(window, imagePath);
            using var lockedTransfer = new DataTransfer();
            lockedTransfer.Add(DataTransferItem.CreateFile(lockedFile));
            var lockedDrop = new DragEventArgs(
                DragDrop.DropEvent,
                lockedTransfer,
                view.image,
                imageCenter,
                KeyModifiers.None);
            framePanel.RaiseEvent(lockedDrop);
            await HeadlessTestHelpers.WaitUntilAsync(() => notifications.All.Count >= 1);

            editor.Scene.Layers.Single(layer => layer.ZIndex.Value == 0).IsLocked.Value = false;
            using IStorageFile unsupportedFile = await GetStorageFile(window, unsupportedPath);
            using var unsupportedTransfer = new DataTransfer();
            unsupportedTransfer.Add(DataTransferItem.CreateFile(unsupportedFile));
            var unsupportedDrop = new DragEventArgs(
                DragDrop.DropEvent,
                unsupportedTransfer,
                view.image,
                imageCenter,
                KeyModifiers.None);
            framePanel.RaiseEvent(unsupportedDrop);
            await HeadlessTestHelpers.WaitUntilAsync(() => notifications.All.Count >= 2);
            HeadlessTestHelpers.Settle();

            Notification[] shown = notifications.All.ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(lockedDrop.Handled, Is.True);
                Assert.That(unsupportedDrop.Handled, Is.True);
                Assert.That(editor.Scene.Children, Is.Empty);
                Assert.That(shown[0].Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(shown[0].Title, Is.EqualTo(Strings.Lock));
                Assert.That(shown[0].Message, Is.EqualTo(Strings.LayerIsLocked));
                Assert.That(shown[1].Type, Is.EqualTo(NotificationType.Error));
                Assert.That(shown[1].Title, Is.EqualTo(Strings.AddElement));
                Assert.That(shown[1].Message, Is.EqualTo(MessageStrings.UnexpectedError));
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase("timeline", "locked")]
    [TestCase("timeline", "unsupported")]
    [TestCase("timeline", "partial")]
    [TestCase("player", "locked")]
    [TestCase("player", "unsupported")]
    [TestCase("player", "partial")]
    [TestCase("player-without-timeline", "locked")]
    [TestCase("player-without-timeline", "unsupported")]
    [TestCase("player-without-timeline", "partial")]
    public async Task StorageDropReleasesRejectedCopiesAndRetainsAcceptedResources(string target, string outcome)
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditorForNewScene($"storage-{target}-{outcome}");
        string image = CreatePngFile(editor, "accepted.png");
        string unsupported = Path.ChangeExtension(image, ".drop-test");
        await File.WriteAllTextAsync(unsupported, "unsupported");
        if (outcome == "locked")
            editor.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsLocked = { Value = true } });
        if (target == "player-without-timeline") editor.CloseToolTab(timeline);
        using var notifications = new NotificationCapture();
        var view = target == "timeline" ? (Control)new TimelineTabView { DataContext = timeline } : new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            if (view is PlayerView player)
            {
                editor.Player.PreviewImage.Value = Ref<Bitmap>.Create(new Bitmap(editor.Scene.FrameSize.Value.Width, editor.Scene.FrameSize.Value.Height));
                player.image.Width = editor.Scene.FrameSize.Value.Width;
                player.image.Height = editor.Scene.FrameSize.Value.Height;
            }
            window.Show();
            HeadlessTestHelpers.Render();
            Panel panel = view.FindControl<Panel>(target == "timeline" ? "TimelinePanel" : "framePanel")!;
            using var transfer = new DataTransfer();
            string[] sources = outcome == "partial" ? [image, unsupported] : [outcome == "locked" ? image : unsupported];
            transfer.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("test", new object(), [], sources, () => true, _ => Task.FromResult(false))));
            var args = new DragEventArgs(DragDrop.DropEvent, transfer, panel, new AvaPoint(10, 5), KeyModifiers.None);
            panel.RaiseEvent(args);
            await HeadlessTestHelpers.WaitUntilAsync(() => notifications.All.Count == 1);
            HeadlessTestHelpers.Settle();

            string storage = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "resources", "storage");
            string[] copies = Directory.GetFiles(storage, "*", SearchOption.AllDirectories);
            Assert.That(args.Handled, Is.True);
            Assert.That(editor.Scene.Children, Has.Count.EqualTo(outcome == "partial" ? 1 : 0));
            Assert.That(copies.Select(Path.GetFileName), Is.EqualTo(outcome == "partial" ? new[] { "accepted.png" } : Array.Empty<string>()));
            if (outcome != "partial") Assert.That(Directory.GetDirectories(storage), Is.Empty);
            Assert.That(sources.All(File.Exists), Is.True, "The original drag downloads remain untouched.");
        }
        finally { window.Close(); HeadlessTestHelpers.Settle(); }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task TimelineStorageDropAdvancesPastAllAddedLayers(bool companions, bool rejectFirst)
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditorForNewScene("storage-drop-layers");
        var handler = new LayeredFileSourceHandler(companions);
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await using var registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler, ElementSourceHandlerRegistrationMode.Replace);
        string[] sources = rejectFirst
            ? [CreatePngFile(editor, "rejected.png"), CreatePngFile(editor, "first.png"), CreatePngFile(editor, "second.png")]
            : [CreatePngFile(editor, "first.png"), CreatePngFile(editor, "second.png")];
        using var notifications = new NotificationCapture();
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Panel panel = view.FindControl<Panel>("TimelinePanel")!;
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("test", new object(), [], sources, () => true, _ => Task.FromResult(false))));
            var args = new DragEventArgs(DragDrop.DropEvent, transfer, panel, new AvaPoint(180, 5), KeyModifiers.None);
            panel.RaiseEvent(args);
            int count = companions ? 4 : 2;
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Scene.Children.Count == count);
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Scene.Children.Select(element => element.ZIndex.Value).Order(), Is.EqualTo(Enumerable.Range(0, count)));
            Assert.That(editor.Scene.Children.All(element => element.Start.Value == timeline.ClickedFrame), Is.True);
            Assert.That(handler.RequestedLayers, Is.EqualTo(rejectFirst ? new[] { 0, 0, 2 } : new[] { 0, companions ? 2 : 1 }));
            Assert.That(notifications.All, Has.Count.EqualTo(rejectFirst ? 1 : 0));
        }
        finally { window.Close(); HeadlessTestHelpers.Settle(); }
    }

    [AvaloniaTest]
    [TestCase(false, "success", false)]
    [TestCase(true, "success", false)]
    [TestCase(false, "cancel", false)]
    [TestCase(true, "cancel", false)]
    [TestCase(false, "signout", false)]
    [TestCase(true, "signout", false)]
    [TestCase(false, "busy", false)]
    [TestCase(true, "busy", false)]
    [TestCase(false, "unsupported", false)]
    [TestCase(true, "unsupported", false)]
    [TestCase(false, "success", true)]
    [TestCase(true, "success", true)]
    [TestCase(false, "busy", true)]
    [TestCase(true, "busy", true)]
    public async Task StorageEditorDropsCleanStagingAfterSuccessOrRejection(bool playerTarget, string outcome, bool nativeReady)
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenEditorForNewScene($"storage-pending-drop-{playerTarget}-{outcome}-{nativeReady}");
        await using var scope = new CloudStorageIncrementalTests.StorageScope();
        await scope.LoadFirstAsync(CloudStorageTests.Response(name: outcome == "unsupported" ? "rejected.png" : "dropped.bin", fileSize: 8));
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await using var registration = PackageSourceHandlers.Register(editor.ExtensionProvider, new LayeredFileSourceHandler(false), ElementSourceHandlerRegistrationMode.Replace);
        var source = new CloudStorageView { DataContext = scope.ViewModel };
        Control target = playerTarget ? new PlayerView { DataContext = editor.Player } : new TimelineTabView { DataContext = timeline };
        editor.Player.CurrentFrame.Value = MediaTime.FromSeconds(4);
        if (target is PlayerView player)
        {
            editor.Player.PreviewImage.Value = Ref<Bitmap>.Create(new Bitmap(editor.Scene.FrameSize.Value.Width, editor.Scene.FrameSize.Value.Height));
            player.image.Width = editor.Scene.FrameSize.Value.Width;
            player.image.Height = editor.Scene.FrameSize.Value.Height;
        }
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*") };
        Grid.SetColumn(target, 1);
        layout.Children.Add(source);
        layout.Children.Add(target);
        var window = new Window { Content = layout, Width = 1200, Height = 700 };
        using var notifications = new NotificationCapture();
        string downloads = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "storage", "downloads");
        string[] originalDownloads = Directory.Exists(downloads) ? Directory.GetDirectories(downloads) : [];
        using var reservation = outcome == "busy" ? TestShell.Editor.TryBeginWorktreeMutation() : null;
        if (outcome == "busy") Assert.That(reservation, Is.Not.Null);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var list = source.FindControl<ListBox>("StorageItems")!;
            AvaPoint start = list.ContainerFromIndex(1)!.TranslatePoint(new AvaPoint(20, 20), window)!.Value;
            Control surface = target is PlayerView preview ? preview.image : target.FindControl<Panel>("TimelinePanel")!;
            AvaPoint end = surface.TranslatePoint(new AvaPoint(80, 5), window)!.Value;
            MediaTime droppedFrame = default;
            void CaptureDropPosition()
            {
                droppedFrame = playerTarget ? editor.Player.CurrentFrame.Value : timeline.ClickedFrame;
                timeline.ClickedFrame = editor.Player.CurrentFrame.Value = MediaTime.FromSeconds(99);
            }
            source.DragStarter = (_, data) =>
            {
                Assert.That(nativeReady, Is.True, "A released pointer must not start a native drag.");
                surface.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, surface, new AvaPoint(80, 5), KeyModifiers.None)
                { DragEffects = DragDropEffects.Copy });
                CaptureDropPosition();
                return Task.FromResult(DragDropEffects.Copy);
            };
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            await HeadlessTestHelpers.WaitUntilAsync(() => scope.Handler.Requests.Count == 2);
            if (!nativeReady)
            {
                window.MouseUp(end, MouseButton.Left);
                CaptureDropPosition();
            }
            Assert.That(scope.Handler.Requests[1].Token.IsCancellationRequested, Is.EqualTo(outcome == "busy" && !nativeReady));
            Assert.That(editor.Scene.Children, Is.Empty);
            if (outcome == "cancel") scope.ViewModel.CancelTransfer.Execute();
            if (outcome == "signout") CloudStorageTests.SignIn(scope.Clients, null);
            scope.Handler.Requests[1].Complete("exported");
            string storage = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "resources", "storage");
            if (outcome == "success")
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => editor.Scene.Children.Count == 1);
                Assert.That(editor.Scene.Children[0].Start.Value, Is.EqualTo(droppedFrame));
                Assert.That(File.ReadAllText(Directory.GetFiles(storage, "*.bin", SearchOption.AllDirectories).Single()), Is.EqualTo("exported"));
            }
            else
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => !scope.ViewModel.IsTransferring.Value);
                if (outcome is "busy" or "unsupported") await HeadlessTestHelpers.WaitUntilAsync(() => notifications.All.Count == 1);
                HeadlessTestHelpers.Settle();
                Assert.That(editor.Scene.Children, Is.Empty);
                Assert.That(!Directory.Exists(storage) || !Directory.EnumerateFileSystemEntries(storage).Any(), Is.True);
            }
            await HeadlessTestHelpers.WaitUntilAsync(() => !Directory.Exists(downloads) || !Directory.GetDirectories(downloads).Except(originalDownloads).Any());
            Assert.That(notifications.All, Has.Count.EqualTo(outcome is "busy" or "unsupported" ? 1 : 0));
            Assert.That(scope.ViewModel.ActionError.Value, Is.Null);
        }
        finally { window.MouseUp(new AvaPoint(0, 0), MouseButton.Left); window.Close(); HeadlessTestHelpers.Settle(); }
    }

    [Test]
    public async Task PlayerDropContainsEditorLifecycleCancellation()
    {
        var description = new ElementDescription(
            MediaTime.Zero,
            MediaTime.FromSeconds(1),
            0,
            new ElementSource.EngineObject(() => new RectShape()));

        foreach (Exception failure in new Exception[]
                 {
                     new OperationCanceledException(),
                     new ObjectDisposedException("editor"),
                 })
        {
            ElementAddResult? result = await PlayerView.AddPlayerDropAsync(
                new FailingElementAdder(failure),
                description);
            Assert.That(result, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task PlayerDropThroughTimelineAwaitsAndContainsEditorLifecycleCancellation()
    {
        await TestReset.ResetShellAsync();
        (EditViewModel editor, _) = await OpenEditorForNewScene("player-timeline-drop-cancellation");
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        var handler = new BlockingFileSourceHandler();
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await using IAsyncDisposable registration = PackageSourceHandlers.Register(editor.ExtensionProvider, handler, ElementSourceHandlerRegistrationMode.Replace);
        var view = new PlayerView();
        var description = new ElementDescription(
            MediaTime.Zero,
            MediaTime.FromSeconds(1),
            0,
            new ElementSource.File("pending.png"));

        Task drop = view.AddElement(editor, description);
        await handler.PreflightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(drop.IsCompleted, Is.False);

        await TestShell.Editor.CloseTabItem(tab);
        await drop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(handler.MaterializationCalled, Is.False);
            Assert.That(editor.Scene, Is.Null);
        });
    }

    private static string CreateElementTemplateFile(EditViewModel editor, string fileName)
    {
        var source = new Element
        {
            Start = { Value = MediaTime.FromSeconds(20) },
            Length = { Value = MediaTime.FromSeconds(7) },
            ZIndex = { Value = 8 },
            Name = { Value = "Dropped template" },
        };
        source.AddObject(new RectShape());
        ObjectTemplateItem template = ObjectTemplateItem.CreateFromInstance(source, "Drop template");
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, fileName);
        File.WriteAllText(
            path,
            ObjectTemplateItem.ToJson(template).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string CreatePngFile(EditViewModel editor, string fileName)
    {
        const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        string path = Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(Png));
        return path;
    }

    private static async Task<IStorageFile> GetStorageFile(Window window, string path)
    {
        return await window.StorageProvider.TryGetFileFromPathAsync(path)
               ?? throw new InvalidOperationException($"The headless storage provider did not resolve '{path}'.");
    }

    private static async Task<Element> RaiseDropAndWaitForElement(
        Scene scene,
        Control target,
        DragEventArgs args)
    {
        var completion = new TaskCompletionSource<Element>(TaskCreationOptions.RunContinuationsAsynchronously);
        NotifyCollectionChangedEventHandler handler = (_, eventArgs) =>
        {
            if (eventArgs.Action != NotifyCollectionChangedAction.Add
                || eventArgs.NewItems?.OfType<Element>().FirstOrDefault() is not { } element)
            {
                return;
            }

            completion.TrySetResult(element);
        };
        scene.Children.CollectionChanged += handler;
        try
        {
            target.RaiseEvent(args);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            scene.Children.CollectionChanged -= handler;
        }
    }

    private sealed class FailingElementAdder(Exception failure) : IElementAdder
    {
        public ValueTask<ElementAddResult> AddAsync(
            IReadOnlyList<ElementDescription> descriptions,
            CancellationToken cancellationToken)
            => ValueTask.FromException<ElementAddResult>(failure);
    }

    private sealed class LayeredFileSourceHandler(bool companions) : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.File);

        public List<int> RequestedLayers { get; } = [];

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(ElementSourcePreflightContext context, CancellationToken cancellationToken)
        {
            int layer = context.Description.Layer;
            RequestedLayers.Add(layer);
            if (Path.GetFileName(((ElementSource.File)context.Description.Source).FileName) == "rejected.png")
                return ValueTask.FromResult(ElementSourcePreflightResult.Rejected(new UnsupportedElementSourceFailure(SourceType)));
            return ValueTask.FromResult(ElementSourcePreflightResult.Ready(new Preflight(), companions ? [layer, layer + 1] : [layer]));
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(ElementSourceMaterializationContext context, IElementSourcePreflight preflight, CancellationToken cancellationToken)
        {
            Element Create(int layer) => new() { Start = { Value = context.Description.Start }, Length = { Value = context.Description.Length!.Value }, ZIndex = { Value = layer } };
            int layer = context.Description.Layer;
            return ValueTask.FromResult(ElementSourceMaterializationResult.Materialized(new ElementMaterialization(Create(layer), companions ? [Create(layer + 1)] : [])));
        }

        private sealed class Preflight : IElementSourcePreflight
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingFileSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(ElementSource.File);

        public TaskCompletionSource PreflightStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool MaterializationCalled { get; private set; }

        public async ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
        {
            PreflightStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertionException("Canceled preflight unexpectedly resumed.");
        }

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
        {
            MaterializationCalled = true;
            throw new AssertionException("Canceled preflight must not be materialized.");
        }
    }
}
