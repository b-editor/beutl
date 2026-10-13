using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A proxy store under the test work directory that holds a Ready proxy of a test video.
internal sealed class ProxyTestScope : IDisposable
{
    private ProxyTestScope(string rootPath, string originalPath, ProxyStore store)
    {
        RootPath = rootPath;
        OriginalPath = originalPath;
        Store = store;
    }

    public string RootPath { get; }

    public string OriginalPath { get; }

    public ProxyStore Store { get; }

    public static ProxyTestScope Create(PixelSize originalSize, PixelSize proxySize, ProxyPreset preset)
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var store = new ProxyStore(root);
        string original = TestMediaHelper.CreateTestVideoFile(originalSize.Width, originalSize.Height, new Rational(30, 1), 30);
        File.WriteAllBytes(original, [1, 2, 3, 4]);
        string proxyTemplate = TestMediaHelper.CreateTestVideoFile(proxySize.Width, proxySize.Height, new Rational(30, 1), 30);
        File.WriteAllBytes(proxyTemplate, [1, 2, 3, 4]);
        string relative = $"proxy/{Path.GetFileName(proxyTemplate)}";
        string proxy = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
        File.Copy(proxyTemplate, proxy, overwrite: true);

        var now = DateTime.UtcNow;
        store.Register(new ProxyEntry(
            ProxyFingerprint.FromFile(original),
            preset,
            ProxyState.Ready,
            relative.Replace(Path.DirectorySeparatorChar, '/'),
            new FileInfo(proxy).Length,
            originalSize,
            proxySize,
            now,
            now,
            null));

        return new ProxyTestScope(root, original, store);
    }

    // The test videos are shared temp files that other fixtures use under the same names, so they stay.
    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
