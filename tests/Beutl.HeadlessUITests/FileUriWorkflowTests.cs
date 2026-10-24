using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Editor.Components.MissingMediaTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class FileUriWorkflowTests
{
    [AvaloniaTest]
    [TestCase("clip#1")]
    [TestCase("clip%41")]
    [TestCase("two words")]
    [TestCase("clip #1 %20")]
    public async Task Import_DecodesMediaAndSavesElementReferences(string name)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(name);
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        string path = Path.Combine(directory, name + ".gif");
        File.WriteAllBytes(path, CreateGif());
        try
        {
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            ElementAddResult result = await adder.AddAsync([
                new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(1), 0, new ElementSource.File(path))
            ], CancellationToken.None);
            HeadlessTestHelpers.Settle();

            Assert.That(result.IsSuccess, Is.True, result.Failure?.ToString());
            Element element = result.Elements.Single();
            var image = (SourceImage)element.Objects.Single();
            Assert.That(image.Source.Value!.Uri.LocalPath, Is.EqualTo(path));
            AssertImageDecodes(image.Source.Value);
            Assert.That(element.Uri!.LocalPath, Is.EqualTo(Path.Combine(directory, $"{element.Id.Value:N}.belm")));

            Assert.That(await editor.SaveAsync(), Is.True);
            Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
            Assert.That(restored.Children.Single().Uri!.LocalPath, Is.EqualTo(element.Uri.LocalPath));
            var restoredImage = (SourceImage)restored.Children.Single().Objects.Single();
            Assert.That(restoredImage.Source.Value!.Uri.LocalPath, Is.EqualTo(path));
            AssertImageDecodes(restoredImage.Source.Value);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase("clip#1")]
    [TestCase("clip%41")]
    [TestCase("two words")]
    [TestCase("clip #1 %20")]
    public async Task Relink_ResolvesAndDecodesProxyForTheSelectedFile(string name)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("file-uri-proxy");
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        var source = VideoSource.Open(Path.Combine(directory, "missing.gif"));
        await TestShell.AddElementAsync(editor, () => new SourceVideo { Source = { Value = source } },
            MediaTime.Zero, MediaTime.FromSeconds(1));
        string path = Path.Combine(directory, name + ".gif");
        File.WriteAllBytes(path, CreateGif());
        var store = new ProxyStore(Path.Combine(directory, "proxy #1 %41 store"));
        string proxyName = name + ".gif";
        string proxyPath = Path.Combine(store.StoreRootPath, proxyName);
        File.WriteAllBytes(proxyPath, CreateGif());
        DateTime now = DateTime.UtcNow;
        store.Register(new ProxyEntry(ProxyFingerprint.FromFile(path), ProxyPreset.Quarter, ProxyState.Ready,
            proxyName, new FileInfo(proxyPath).Length, new PixelSize(1, 1), new PixelSize(1, 1), now, now, null));
        IProxyResolver? previous = DecoderRegistry.ProxyResolver;
        DecoderRegistry.ProxyResolver = new ProxyResolver(store);
        try
        {
            using var tool = new MissingMediaViewModel(editor);
            await tool.SetReplacementAsync(tool.Rows.Single(), path);
            await tool.WaitForCandidatesAsync();
            Assert.That(await tool.ApplyAsync(), Is.True, tool.Error.Value);

            Assert.That(source.Uri.LocalPath, Is.EqualTo(path));
            AssertDecodes(source, preferProxy: false);
            AssertDecodes(source, preferProxy: true, proxyPath);

            Scene restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
            var restoredVideo = (SourceVideo)restored.Children.Single().Objects.Single();
            AssertDecodes(restoredVideo.Source.Value!, preferProxy: true, proxyPath);
        }
        finally
        {
            DecoderRegistry.ProxyResolver = previous;
            await TestReset.ResetShellAsync();
        }
    }

    private static void AssertImageDecodes(ImageSource source)
    {
        using var resource = source.ToResource(new CompositionContext(MediaTime.Zero) { DisableResourceShare = true });
        Assert.That(resource.IsOffline, Is.False);
        Assert.That((resource.Bitmap!.Width, resource.Bitmap.Height), Is.EqualTo((1, 1)));
    }

    private static void AssertDecodes(VideoSource source, bool preferProxy, string? proxyPath = null)
    {
        using var resource = source.ToResource(new CompositionContext(MediaTime.Zero)
        {
            PreferProxy = preferProxy,
            DisableResourceShare = true,
        });
        Assert.That(resource.IsOffline, Is.False);
        Assert.That(resource.ProxyResolution is not null, Is.EqualTo(preferProxy));
        if (preferProxy)
        {
            Assert.That(resource.ProxyResolution!.AbsoluteProxyFilePath, Is.EqualTo(proxyPath));
            Assert.That(resource.ProxyResolution.Source.AbsolutePath,
                Is.EqualTo(ProxyFingerprint.FromFile(source.Uri.LocalPath).AbsolutePath));
        }
        Assert.That(resource.Read(0, out var frame), Is.True);
        using (frame) Assert.That((frame!.Value.Width, frame.Value.Height), Is.EqualTo((1, 1)));
    }

    private static byte[] CreateGif()
    {
        byte[] frame = [0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0];
        return [.. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, .. frame, .. frame, 0x3B];
    }
}
