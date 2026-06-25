using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ExportTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorWithRectangle(string name)
    {
        Project project = (await ProjectService.Current.CreateProject(
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        scene.Duration = TimeSpan.FromMilliseconds(200);

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)EditorService.Current.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromMilliseconds(200),
            Layer: 0,
            EngineObjectFactory: () => new RectShape { Width = { CurrentValue = 200 }, Height = { CurrentValue = 150 } }));
        HeadlessTestHelpers.Settle();
        return editor;
    }

    // ---- B2 (a): non-gated — construct/validate the export ViewModel without spawning a worker ----

    [AvaloniaTest]
    public async Task OutputViewModel_constructs_with_sane_defaults()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("exportvm");

        using var output = new OutputViewModel(editor);
        HeadlessTestHelpers.Settle();

        Assert.That(output.Model, Is.SameAs(editor.Scene));
        Assert.That(output.DestinationFile.Value, Is.Null);
        Assert.That(output.SelectedEncoder.Value, Is.Null);
        Assert.That(output.SupersampleFactor.Value, Is.EqualTo(1));
        Assert.That(output.SupersampleFactors, Is.EqualTo(new[] { 1, 2, 4 }));
        // No destination/encoder yet, so encoding is not permitted.
        Assert.That(output.CanEncode.Value, Is.False);
        Assert.That(output.IsEncoding.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task OutputViewModel_flags_a_supersample_factor_that_exceeds_the_buffer_limit()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("exportwarn");
        // 5000 * 4 = 20000 > MaxBufferDimension (16384), so a 4x factor overflows the buffer.
        editor.Scene.FrameSize = new Media.PixelSize(5000, 240);

        using var output = new OutputViewModel(editor);
        HeadlessTestHelpers.Settle();
        Assert.That(output.SupersampleWarning.Value, Is.Null);

        output.SupersampleFactor.Value = 4;
        HeadlessTestHelpers.Settle();
        Assert.That(output.SupersampleWarning.Value, Is.Not.Null);
        Assert.That(output.CanEncode.Value, Is.False);

        output.SupersampleFactor.Value = 1;
        HeadlessTestHelpers.Settle();
        Assert.That(output.SupersampleWarning.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task OutputViewModel_without_a_registered_encoder_cannot_encode()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("exportnoenc");

        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(NewWorkspace("exportnoenc"), "out.mp4");
        HeadlessTestHelpers.Settle();

        // A destination alone is not enough; with no encoder extension loaded the list is empty
        // and CanEncode stays false (SelectedEncoder is still null).
        Assert.That(output.Encoders, Is.Empty);
        Assert.That(output.CanEncode.Value, Is.False);
    }

    // B2 (b) — selecting a real FFmpeg encoder and running the full export is BLOCKED headless, so
    // no such test ships. Three independent blockers:
    //   1. Selecting the encoder builds its settings editor, whose Codec ChoicesProvider enumerates
    //      native FFmpeg codecs; that native load hangs under the headless host.
    //   2. The worker's managed assembly (Beutl.FFmpegWorker.dll) is not in this test's output — only
    //      the apphost is — so the process aborts at launch ("application to execute does not exist").
    //      Deploying it would need a worker ProjectReference, which the GPL/MIT boundary forbids here.
    //   3. The worker IPC drives async NamedPipe/shared-memory I/O that deadlocks against the
    //      single-threaded Avalonia headless dispatcher (the encode hangs indefinitely).
    // The (a) tests above cover the export ViewModel surface reachable without touching native FFmpeg.
}
