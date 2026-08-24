using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Regression tests for the telemetry crash "ArgumentOutOfRangeException (Parameter 'length')" in
// Beutl.Services.UnhandledExceptionHandler (2.0.0-preview.3 / preview.6). "Change to original
// duration" on a clip whose media duration is shorter than one frame floored the new length to
// zero, and the pointer-release resize could submit a pixel width that rounds to zero frames;
// ElementResizeService.Resize handed both straight to Scene.MoveChild, whose exception escaped
// the async-void handler via Task.ThrowAsync and terminated the app. The service boundary now
// floors every request to one frame, so both flows clamp instead of crashing.
[TestFixture]
public class SubFrameResizeClampTests
{
    private static readonly object s_registerLock = new();
    private static bool s_registered;
    private static string? s_tempDir;

    // 10 ms: positive, but 0.3 frames at the 30 fps project rate, so FloorToRate(30) == 0.
    private static readonly TimeSpan SubFrameAudioDuration = TimeSpan.FromMilliseconds(10);

    private static readonly TimeSpan OneFrameAt30 = TimeSpan.FromSeconds(1d / 30);

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        try
        {
            if (s_tempDir != null && Directory.Exists(s_tempDir))
                Directory.Delete(s_tempDir, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort cleanup of the temp fixture; a leftover directory must not fail the run.
        }
    }

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
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    // Primary telemetry stack (4 of 5 sessions):
    // ElementViewModel.OnChangeToOriginalDuration -> ElementResizeService.Resize -> Scene.MoveChild.
    [AvaloniaTest]
    public async Task ChangeToOriginalDuration_SubFrameMedia_ClampsToOneFrameWithoutCrashing()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("original-duration-subframe");
        RegisterSubFrameDecoder();

        string path = CreateSubFrameAudioFile();
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: 0,
            EngineObjectFactory: () => new SourceSound
            {
                Source = { CurrentValue = soundSource },
            }));
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        var timeline = editor.FindToolTab<TimelineTabViewModel>();
        Assert.That(timeline, Is.Not.Null, "The editor must open with a timeline tool tab.");
        var viewModel = timeline!.GetViewModelFor(element);
        Assert.That(viewModel, Is.Not.Null, "The timeline must create an ElementViewModel for the clip.");

        // Preconditions that used to make the crash deterministic:
        // the original duration resolves, is positive, but floors to zero frames.
        Assert.That(viewModel!.HasOriginalDuration(), Is.True);
        Assert.That(element.TryGetOriginalDuration(out TimeSpan original), Is.True);
        Assert.That(original, Is.EqualTo(SubFrameAudioDuration));
        Assert.That(original.FloorToRate(30), Is.EqualTo(TimeSpan.Zero),
            "The scenario requires an original duration shorter than one project frame.");
        Assert.That(timeline.IsRippleEnabled.Value, Is.False,
            "The telemetry crash is the non-ripple path.");

        viewModel.ChangeToOriginalDuration.Execute();

        Exception? surfaced = null;
        try
        {
            HeadlessTestHelpers.Settle(4);
        }
        catch (Exception ex)
        {
            surfaced = Unwrap(ex);
        }

        Assert.That(surfaced, Is.Null,
            $"The resize must be clamped at the service boundary instead of escaping to the dispatcher, but got: {surfaced}");
        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(element.Length, Is.EqualTo(OneFrameAt30),
                "the sub-frame original duration is floored to one frame at the project rate");
        });
    }

    // Second telemetry stack (preview.6, OnBorderPointerReleased -> Resize -> MoveChild): the
    // pointer-release resize submits Width/BorderMargin pixels rounded to frames, and a clip whose
    // visual width rounds to zero frames used to reach Scene.MoveChild with length == 0 through
    // SubmitViewModelChanges.
    [AvaloniaTest]
    public async Task SubmitViewModelChanges_ZeroPixelWidth_ClampsToOneFrameWithoutCrashing()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("resize-zero-width");

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.FromSeconds(1),
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => new Beutl.Graphics.Shapes.RectShape()));
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        var timeline = editor.FindToolTab<TimelineTabViewModel>();
        Assert.That(timeline, Is.Not.Null, "The editor must open with a timeline tool tab.");
        var viewModel = timeline!.GetViewModelFor(element);
        Assert.That(viewModel, Is.Not.Null, "The timeline must create an ElementViewModel for the clip.");
        Assert.That(timeline.IsRippleEnabled.Value, Is.False,
            "The telemetry crash is the non-ripple path.");

        // The drag visual collapsed the clip to zero width; keep the left edge pinned at the
        // element's start so the submission only degenerates the length.
        float scale = timeline.Options.Value.Scale;
        viewModel!.BorderMargin.Value = new Thickness(element.Start.TimeToPixel(scale), 0, 0, 0);
        viewModel.Width.Value = 0;

        Exception? surfaced = null;
        try
        {
            await viewModel.SubmitViewModelChanges();
            HeadlessTestHelpers.Settle(4);
        }
        catch (Exception ex)
        {
            surfaced = Unwrap(ex);
        }

        Assert.That(surfaced, Is.Null,
            $"A zero-length resize submission must be floored at the service boundary, but got: {surfaced}");
        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(OneFrameAt30),
                "the zero-frame width is floored to one frame at the project rate");
        });
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException or AggregateException
               && ex.InnerException is { } inner)
        {
            ex = inner;
        }

        return ex;
    }

    private static void RegisterSubFrameDecoder()
    {
        lock (s_registerLock)
        {
            if (s_registered) return;
            DecoderRegistry.Register(new SubFrameDecoderInfo());
            s_registered = true;
        }
    }

    private static string CreateSubFrameAudioFile()
    {
        s_tempDir ??= Path.Combine(Path.GetTempPath(), $"beutl-subframe-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(s_tempDir);
        string path = Path.Combine(s_tempDir, $"subframe-{Guid.NewGuid():N}.subaudio");
        File.WriteAllBytes(path, []);
        return path;
    }

    private sealed class SubFrameDecoderInfo : IDecoderInfo
    {
        public string Name => "SubFrame Test Decoder";

        public MediaReader? Open(string file, MediaOptions options)
        {
            return Path.GetExtension(file) == ".subaudio" ? new SubFrameReader() : null;
        }

        public IEnumerable<string> VideoExtensions() => [];

        public IEnumerable<string> AudioExtensions() => [".subaudio"];
    }

    private sealed class SubFrameReader : MediaReader
    {
        private readonly AudioStreamInfo _audioInfo = new(
            "test",
            new Rational(SubFrameAudioDuration.Milliseconds, 1000),
            44100,
            2);

        public override VideoStreamInfo VideoInfo => throw new Exception("The stream does not exist.");

        public override AudioStreamInfo AudioInfo => _audioInfo;

        public override bool HasVideo => false;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            // Report end-of-stream with an empty buffer instead of a decode failure (false signals
            // an unrecoverable error): HasAudio stays truthful while sample consumers see silence,
            // matching SampleProviderReader's empty-result pattern.
            sound = Ref<IPcm>.Create(new Pcm<Stereo32BitFloat>(_audioInfo.SampleRate, 0));
            return true;
        }
    }
}
