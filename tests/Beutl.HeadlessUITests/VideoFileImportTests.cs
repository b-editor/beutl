using System.Diagnostics.CodeAnalysis;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Regression tests for #2183: dropping a video without an audio track onto the timeline must not
// add a SourceSound element (which crashed when its resource dereferenced AudioInfo).
[TestFixture]
public class VideoFileImportTests
{
    private static readonly object s_registerLock = new();
    private static bool s_registered;
    private static string? s_tempDir;

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

    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

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

        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        return (EditViewModel)tab.Context.Value;
    }

    [AvaloniaTest]
    public async Task ImportVideoWithoutAudioTrack_AddsOnlyVideoElement()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("videoonly");
        RegisterImportDecoder();
        string path = CreateImportFile("sample", withAudio: false);

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(5),
            Layer: 0,
            FileName: path));
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1),
            "音声トラックのない動画はビデオ要素のみ追加する (#2183)");
        Element element = editor.Scene.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(element.Objects.OfType<SourceVideo>().Any(), Is.True);
            Assert.That(element.Objects.OfType<SourceSound>().Any(), Is.False,
                "音声トラックのない動画に音声要素を追加してはならない (#2183)");
        });
        Assert.That(editor.Scene.Groups, Is.Empty,
            "音声要素がないのでグループ化も発生しない (#2183)");
    }

    [AvaloniaTest]
    public async Task ImportVideoWithAudioTrack_AddsVideoAndSoundElementsAsGroup()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("videoaudio");
        RegisterImportDecoder();
        string path = CreateImportFile("sample", withAudio: true);

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(5),
            Layer: 0,
            FileName: path));
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(2),
            "音声トラックのある動画はビデオ要素と音声要素の両方を追加する");
        Assert.That(editor.Scene.Children.Count(c => c.Objects.OfType<SourceVideo>().Any()), Is.EqualTo(1));
        Assert.That(editor.Scene.Children.Count(c => c.Objects.OfType<SourceSound>().Any()), Is.EqualTo(1));
        Assert.That(editor.Scene.Groups, Has.Count.EqualTo(1),
            "音声要素がある場合は従来どおりグループ化する");
    }

    private static void RegisterImportDecoder()
    {
        lock (s_registerLock)
        {
            if (s_registered) return;
            DecoderRegistry.Register(new ImportTestDecoderInfo());
            s_registered = true;
        }
    }

    private static string CreateImportFile(string stem, bool withAudio)
    {
        s_tempDir ??= Path.Combine(Path.GetTempPath(), $"beutl-import-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(s_tempDir);
        string ext = withAudio ? ".vaudio" : ".vonly";
        string path = Path.Combine(s_tempDir, $"{stem}-{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(path, []);
        return path;
    }

    private sealed class ImportTestDecoderInfo : IDecoderInfo
    {
        public string Name => "Import Test Decoder";

        public MediaReader? Open(string file, MediaOptions options)
        {
            string ext = Path.GetExtension(file);
            return ext switch
            {
                ".vonly" => new ImportTestReader(hasVideo: true, hasAudio: false),
                ".vaudio" => new ImportTestReader(hasVideo: true, hasAudio: true),
                _ => null,
            };
        }

        public IEnumerable<string> VideoExtensions() => [".vonly", ".vaudio"];

        public IEnumerable<string> AudioExtensions() => [];
    }

    // Mirrors the real backends: AudioInfo/VideoInfo throw "The stream does not exist." when the
    // reader was not created with the corresponding stream, exactly like FFmpegReaderProxy.
    private sealed class ImportTestReader : MediaReader
    {
        private readonly VideoStreamInfo? _videoInfo;
        private readonly AudioStreamInfo? _audioInfo;

        public ImportTestReader(bool hasVideo, bool hasAudio)
        {
            HasVideo = hasVideo;
            HasAudio = hasAudio;
            if (hasVideo)
            {
                _videoInfo = new VideoStreamInfo(
                    "test",
                    numFrames: 60,
                    new PixelSize(80, 80),
                    new Rational(30, 1));
            }
            if (hasAudio)
            {
                _audioInfo = new AudioStreamInfo(
                    "test",
                    new Rational(2, 1),
                    44100,
                    2);
            }
        }

        public override VideoStreamInfo VideoInfo => _videoInfo ?? throw new Exception("The stream does not exist.");

        public override AudioStreamInfo AudioInfo => _audioInfo ?? throw new Exception("The stream does not exist.");

        public override bool HasVideo { get; }

        public override bool HasAudio { get; }

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }
    }
}
