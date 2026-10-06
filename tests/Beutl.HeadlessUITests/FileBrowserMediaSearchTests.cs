using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class FileBrowserMediaSearchTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Hidden_outputs_are_not_probed_and_published_outputs_are_discovered(bool nested)
    {
        string root = Directory.CreateTempSubdirectory("beutl-browser-media-").FullName;
        string directory = nested ? Path.Combine(root, "assets") : root;
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.mp4");
        string partial = Path.Combine(directory, $".Project6.beutl-part-{Guid.NewGuid():N}.mp4");
        string stagingDirectory = Path.Combine(directory, $".beutl-output-{Guid.NewGuid():N}");
        string staged = Path.Combine(stagingDirectory, "Project6.mp4");
        string published = Path.Combine(directory, "Project6.mp4");
        Directory.CreateDirectory(stagingDirectory);
        File.WriteAllText(source, "source");
        File.WriteAllText(partial, "unfinished");
        File.WriteAllText(staged, "unfinished");
        File.SetAttributes(partial, File.GetAttributes(partial) | FileAttributes.Hidden);
        File.SetAttributes(stagingDirectory, File.GetAttributes(stagingDirectory) | FileAttributes.Hidden);

        var decoder = new RecordingDecoderInfo();
        DecoderRegistry.Register(decoder);
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(Scene)))
            .Returns(new Scene { Uri = new Uri(Path.Combine(root, "main.scene")) });
        using var browser = new FileBrowserTabViewModel(context.Object);
        var window = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 600,
            Height = 600
        };
        try
        {
            await WaitUntilAsync(() => !browser.IsLoadingMediaFiles.Value && decoder.OpenedPaths.Contains(source));
            Assert.Multiple(() =>
            {
                Assert.That(browser.MediaFileItems.Select(item => item.FullPath), Is.EqualTo(new[] { source }));
                Assert.That(decoder.OpenedPaths, Does.Not.Contain(partial));
                Assert.That(decoder.OpenedPaths, Does.Not.Contain(staged));
            });

            File.WriteAllText(staged, "complete");
            File.Move(staged, published);
            browser.Refresh();
            await WaitUntilAsync(() => !browser.IsLoadingMediaFiles.Value && decoder.OpenedPaths.Contains(published));
            Assert.Multiple(() =>
            {
                Assert.That(browser.MediaFileItems.Select(item => item.FullPath), Is.EquivalentTo(new[] { source, published }));
                Assert.That(decoder.OpenedPaths, Does.Not.Contain(partial));
                Assert.That(decoder.OpenedPaths, Does.Not.Contain(staged));
            });

            window.Show();
            HeadlessTestHelpers.Render();
            if (Environment.GetEnvironmentVariable("BEUTL_FILE_BROWSER_MEDIA_CAPTURE") is { Length: > 0 } capture)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.ChangeExtension(capture, nested ? "nested.png" : "root.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            browser.Dispose();
            DecoderRegistry.Unregister(decoder);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                Assert.Fail("Timed out waiting for media search.");
            await Task.Delay(10);
            HeadlessTestHelpers.Settle();
        }
        HeadlessTestHelpers.Settle();
    }

    private sealed class RecordingDecoderInfo : IDecoderInfo
    {
        public ConcurrentQueue<string> OpenedPaths { get; } = new();

        public string Name => "Recording Media Search Decoder";

        public MediaReader? Open(string file, MediaOptions options)
        {
            OpenedPaths.Enqueue(file);
            return null;
        }

        public IEnumerable<string> VideoExtensions() => [".mp4"];

        public IEnumerable<string> AudioExtensions() => [];
    }
}
