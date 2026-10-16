using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Media.Decoding;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.UnitTests.Editor;

[TestFixture]
[NonParallelizable]
public class FileSystemItemViewModelDecoderTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-fsitem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // Every container the browser classified before extensions were consulted must keep its icon
    // on an install where the decoder that reads it is absent.
    [TestCase("song.flac", nameof(Icon.MusicNote1))]
    [TestCase("clip.mp4", nameof(Icon.Video))]
    [TestCase("voice.mp3", nameof(Icon.MusicNote1))]
    [TestCase("photo.png", nameof(Icon.Image))]
    public void IconSymbol_DoesNotDependOnARegisteredDecoder(string fileName, string expected)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, [0]);

        using var item = new FileSystemItemViewModel(path, false);

        Assert.That(item.IconSymbol.Value.ToString(), Is.EqualTo(expected));
    }

    // '.ts' is a transport stream to the FFmpeg decoder and TypeScript to everyone else; the named
    // extension has to win so source files do not get handed to a video decoder.
    [Test]
    public void IconSymbol_TypeScript_StaysCode()
    {
        var decoder = new TransportStreamDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            string path = Path.Combine(_root, "app.ts");
            File.WriteAllBytes(path, [0]);

            using var item = new FileSystemItemViewModel(path, false);

            Assert.That(item.IconSymbol.Value, Is.EqualTo(Icon.Code));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    // The placeholder that gives a collapsed folder its expand arrow must not read the folder as a file:
    // describing it would throw inside for every collapsed folder in the list.
    [Test]
    public void CollapsedFolderPlaceholder_DoesNotReadTheFolder()
    {
        string folder = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "child.txt"), [0]);
        bool readAsFile = false;
        void OnFirstChance(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
            => readAsFile |= e.Exception is FileNotFoundException { FileName: var name } && name == folder;

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        try
        {
            using var item = new FileSystemItemViewModel(folder, true);
            Assert.That(item.Children, Has.Count.EqualTo(1));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        }

        Assert.That(readAsFile, Is.False);
    }

    private sealed class TransportStreamDecoderInfo : IDecoderInfo
    {
        public string Name => "Transport Stream";

        public MediaReader? Open(string file, MediaOptions options) => null;

        public IEnumerable<string> VideoExtensions() => [".ts"];

        public IEnumerable<string> AudioExtensions() => [];
    }
}
