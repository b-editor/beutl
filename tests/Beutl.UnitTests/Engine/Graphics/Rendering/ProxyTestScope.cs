using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A proxy store under the test work directory that holds a test video and, unless created without one, a Ready
// proxy of it.
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
        ProxyTestScope scope = CreateWithoutProxy(originalSize);
        RegisterTestVideoProxy(scope.Store, scope.OriginalPath, originalSize, proxySize, preset);
        return scope;
    }

    public static ProxyTestScope CreateWithoutProxy(PixelSize originalSize)
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        string original = TestMediaHelper.CreateTestVideoFile(originalSize.Width, originalSize.Height, new Rational(30, 1), 30);
        File.WriteAllBytes(original, [1, 2, 3, 4]);
        return new ProxyTestScope(root, original, store);
    }

    // Copies a test video of proxySize, at the original's rate and length, into the store and registers it as the
    // Ready proxy of originalPath. The original must not be empty, because its fingerprint reads it.
    public static void RegisterTestVideoProxy(
        ProxyStore store, string originalPath, PixelSize originalSize, PixelSize proxySize, ProxyPreset preset)
    {
        (_, _, Rational frameRate, int frameCount) = TestMediaHelper.ParseTestVideoPath(originalPath);
        string proxyTemplate = TestMediaHelper.CreateTestVideoFile(proxySize.Width, proxySize.Height, frameRate, frameCount);
        File.WriteAllBytes(proxyTemplate, [1, 2, 3, 4]);
        string relative = $"proxy/{Path.GetFileName(proxyTemplate)}";
        string proxy = Path.Combine(store.StoreRootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
        File.Copy(proxyTemplate, proxy, overwrite: true);

        var now = DateTime.UtcNow;
        store.Register(new ProxyEntry(
            ProxyFingerprint.FromFile(originalPath),
            preset,
            ProxyState.Ready,
            relative,
            new FileInfo(proxy).Length,
            originalSize,
            proxySize,
            now,
            now,
            null));
    }

    // An empty directory under the test work directory.
    public static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // The test videos are shared temp files that other fixtures use under the same names, so they stay.
    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
