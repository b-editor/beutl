using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class EditViewStateRestoreTests
{
    [AvaloniaTest]
    [TestCase("{", null)]
    [TestCase("[]", null)]
    [TestCase("null", null)]
    [TestCase("{}", typeof(InvalidOperationException))]
    [TestCase("{}", typeof(OperationCanceledException))]
    public async Task A_layout_that_cannot_be_restored_opens_the_scene_and_is_replaced_on_close(
        string content, Type? failure)
    {
        string stateFile = await OpenAndClose(content, failure);

        Assert.That(JsonNode.Parse(File.ReadAllText(stateFile))?["scale"], Is.Not.Null);
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(stateFile)!, "*.corrupt-*"), Is.Empty);
    }

    [AvaloniaTest]
    public async Task A_layout_file_that_cannot_be_read_is_not_replaced_in_that_session()
    {
        const string Content = """{"scale":2}""";
        string stateFile = await OpenAndClose(Content, failure: null, lockWhileOpening: true);

        Assert.That(File.ReadAllText(stateFile), Is.EqualTo(Content));
    }

    // The layout file belongs to the project, so its playhead and beat offset read on its frame grid (#2885).
    [AvaloniaTest]
    public async Task The_saved_playhead_and_beat_offset_read_on_the_frame_grid()
    {
        string workspace = TestShell.NewWorkspace("view-state-" + Guid.NewGuid().ToString("N"));
        var scene = new Scene(320, 180, "view-state") { Uri = new Uri(Path.Combine(workspace, "main.scene")) };
        string stateFile = Path.Combine(EditorConstants.GetViewStateDirectory(scene.Uri), "main.config");
        File.WriteAllText(stateFile, """{"current-time":"00:00:01.0333333","bpm-grid":{"offset":"00:00:01.0333333"}}""");
        var extensions = new ExtensionProvider();
        var editor = new EditViewModel(scene, extensions, new EditorService(extensions));
        try
        {
            MediaTime frame = MediaTime.FromFrames(31, new Rational(30));
            var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
            var options = (ITimelineOptionsProvider)editor.GetService(typeof(ITimelineOptionsProvider))!;

            Assert.Multiple(() =>
            {
                Assert.That(clock.CurrentTime.Value, Is.EqualTo(frame));
                Assert.That(options.Options.Value.BpmGrid.Offset, Is.EqualTo(frame));
            });
        }
        finally
        {
            await editor.DisposeAsync();
        }
    }

    // The scroll viewer reports its first offset on its first layout, which must not replace the saved one.
    [AvaloniaTest]
    public async Task A_reopened_scene_shows_the_timeline_where_it_was_scrolled()
    {
        (Project project, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("view-state-scroll");
        var saved = new Vector2(1200, 300);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Offset = saved };
        await editor.SaveAsync();

        editor = await TestShell.ReopenAsync(project.Uri!.LocalPath);
        timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        var view = new TimelineTabView { DataContext = timeline };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(timeline.Options.Value.Offset, Is.EqualTo(saved));
                Assert.That(view.FindControl<ScrollViewer>("ContentScroll")!.Offset, Is.EqualTo(new Avalonia.Vector(saved.X, saved.Y)));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Opens a scene whose layout file holds content, with a default tool tab that throws failure, and closes it.
    private static async Task<string> OpenAndClose(string content, Type? failure, bool lockWhileOpening = false)
    {
        string workspace = TestShell.NewWorkspace("view-state-" + Guid.NewGuid().ToString("N"));
        var scene = new Scene(320, 180, "view-state") { Uri = new Uri(Path.Combine(workspace, "main.scene")) };
        string stateFile = Path.Combine(EditorConstants.GetViewStateDirectory(scene.Uri), "main.config");
        File.WriteAllText(stateFile, content);
        var extensions = new ExtensionProvider();
        if (failure != null)
            extensions.AddExtensions(1, [new FailingToolTabExtension((Exception)Activator.CreateInstance(failure)!)]);

        EditViewModel editor;
        using (lockWhileOpening ? new FileStream(stateFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null)
            editor = new EditViewModel(scene, extensions, new EditorService(extensions));
        await editor.DisposeAsync();
        return stateFile;
    }

    private sealed class FailingToolTabExtension(Exception failure) : ToolTabExtension
    {
        public override string Name => "Failing test tool";

        public override string DisplayName => "Failing test tool";

        public override bool CanMultiple => false;

        public override bool OpenByDefault => true;

        public override bool TryCreateContent(
            IEditorContext editorContext, [NotNullWhen(true)] out Control? control) => throw failure;

        public override bool TryCreateContext(
            IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context) => throw failure;
    }
}
