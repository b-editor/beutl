using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ExportTests
{
    private sealed class TestCoreObject : CoreObject;

    private sealed class TestOutputContext(string fileName) : IOutputContext
    {
        private readonly ReactivePropertySlim<bool> _isEncoding = new();
        private int _disposeCount;

        public OutputExtension Extension => SceneOutputExtension.Instance;

        public CoreObject Object { get; } = new TestCoreObject
        {
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, fileName))
        };

        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>(fileName);

        public IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }
            = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<bool> IsEncoding => _isEncoding;

        public IReadOnlyReactiveProperty<double> Progress { get; }
            = new ReactivePropertySlim<double>();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public event EventHandler? Started;

        public event EventHandler? Finished;

        public void Start()
        {
            _isEncoding.Value = true;
            Started?.Invoke(this, EventArgs.Empty);
        }

        public void Finish()
        {
            _isEncoding.Value = false;
            Finished?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }

        public void WriteToJson(JsonObject json)
        {
        }

        public void ReadFromJson(JsonObject json)
        {
        }
    }

    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorWithRectangle(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        scene.Duration = TimeSpan.FromMilliseconds(200);

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

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

    [AvaloniaTest]
    public async Task OutputProfileItem_dispose_during_encoding_waits_for_finished()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-active");
        var context = new TestOutputContext("export-dispose-active.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);

        context.Start();
        try
        {
            item.Dispose();

            Assert.That(context.DisposeCount, Is.Zero);
            AssertWorktreeMutationBlocked();

            context.Finish();

            Assert.That(context.DisposeCount, Is.EqualTo(1));
            AssertWorktreeMutationAvailable();
        }
        finally
        {
            context.Finish();
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputTabViewModel_remove_item_refuses_active_encoding()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-remove-active");
        var viewModel = new OutputTabViewModel(editor);
        var context = new TestOutputContext("export-remove-active.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        viewModel.Items.Add(item);
        viewModel.SelectedItem.Value = item;

        context.Start();
        try
        {
            viewModel.RemoveItem(item);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items, Does.Contain(item));
                Assert.That(viewModel.SelectedItem.Value, Is.SameAs(item));
                Assert.That(context.DisposeCount, Is.Zero);
            });
            AssertWorktreeMutationBlocked();

            context.Finish();
            viewModel.RemoveItem(item);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items, Does.Not.Contain(item));
                Assert.That(context.DisposeCount, Is.EqualTo(1));
            });
            AssertWorktreeMutationAvailable();
        }
        finally
        {
            context.Finish();
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_dispose_and_finished_race_disposes_once()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-race");
        var context = new TestOutputContext("export-dispose-race.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        using var gate = new ManualResetEventSlim();

        context.Start();
        Task disposeTask = Task.Run(() =>
        {
            gate.Wait();
            item.Dispose();
        });
        Task finishTask = Task.Run(() =>
        {
            gate.Wait();
            context.Finish();
        });

        gate.Set();
        await Task.WhenAll(disposeTask, finishTask);
        item.Dispose();
        context.Finish();

        Assert.That(context.DisposeCount, Is.EqualTo(1));
        AssertWorktreeMutationAvailable();
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_dispose_when_idle_is_immediate_and_idempotent()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-idle");
        var context = new TestOutputContext("export-dispose-idle.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);

        item.Dispose();
        item.Dispose();

        Assert.That(context.DisposeCount, Is.EqualTo(1));
        AssertWorktreeMutationAvailable();
    }

    private static void AssertWorktreeMutationBlocked()
    {
        IDisposable? operation = TestShell.Editor.TryBeginWorktreeMutation();
        try
        {
            Assert.That(operation, Is.Null);
        }
        finally
        {
            operation?.Dispose();
        }
    }

    private static void AssertWorktreeMutationAvailable()
    {
        IDisposable? operation = TestShell.Editor.TryBeginWorktreeMutation();
        try
        {
            Assert.That(operation, Is.Not.Null);
        }
        finally
        {
            operation?.Dispose();
        }
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
