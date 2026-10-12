using System.Runtime.Versioning;
using Beutl.Embedding.MediaFoundation.Decoding;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using SharpGen.Runtime.Diagnostics;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
[Platform("Win")]
[NonParallelizable]
[SupportedOSPlatform("windows")]
public class MFDecoderLifecycleTests
{
    private string _workDir = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // SharpGen's Configuration is a process-global that becomes immutable on first use (the first
        // COM wrapper / ObjectTracker access). Enable tracking here, before this fixture creates any
        // Media Foundation COM object, so the baseline count sees the wrappers the MFDecoder ctor
        // creates. MFThread sets the same flags for opens on the application's dispatcher. These flags
        // are one-way switches that cannot be restored afterwards, so OneTimeTearDown leaves them as-is.
        SharpGen.Runtime.Configuration.EnableObjectTracking = true;
        SharpGen.Runtime.Configuration.EnableReleaseOnFinalizer = true;
        SharpGen.Runtime.Configuration.UseThreadStaticObjectTracking = true;
        MediaFactory.MFStartup();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // The tracking flags cannot be restored (see OneTimeSetUp), so only Media Foundation is shut down.
        MediaFactory.MFShutdown();
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-mf-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Test]
    public void Constructor_NoVideoStream_DisposesPartiallyCreatedComObjects()
    {
        string wav = SineWav.Write(_workDir);
        int before = CountTrackedMediaFoundationObjects();

        Assert.Throws<NoVideoStreamException>(() =>
            _ = new MFDecoder(wav, new MFDecodingExtension()));

        int after = CountTrackedMediaFoundationObjects();
        Assert.That(after, Is.EqualTo(before),
            "MFDecoder must deterministically dispose COM wrappers created before constructor failure.");
    }

    private static int CountTrackedMediaFoundationObjects()
    {
        int count = 0;
        foreach (WeakReference<CppObject> reference in ObjectTracker.FindActiveObjects())
        {
            if (reference.TryGetTarget(out CppObject? target)
                && target.GetType().Namespace?.StartsWith("Vortice.MediaFoundation", StringComparison.Ordinal) == true)
            {
                count++;
            }
        }

        return count;
    }

    [Test]
    public void Constructor_UnsupportedByteStream_LogsDebugAndDisposesCreatedComObjects()
    {
        string file = Path.Combine(_workDir, "unsupported.mp4");
        File.WriteAllText(file, "This is not a recognized media byte stream.");
        var logger = new CaptureLogger();
        int before = CountTrackedMediaFoundationObjects();

        SharpGenException? exception = Assert.Throws<SharpGenException>(() =>
            _ = new MFDecoder(file, new MFDecodingExtension(), logger));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.ResultCode.Code, Is.EqualTo(unchecked((int)0xC00D36C4)));
            Assert.That(logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Debug }));
            Assert.That(logger.Entries[0].Exception, Is.SameAs(exception));
            Assert.That(CountTrackedMediaFoundationObjects(), Is.EqualTo(before),
                "Unsupported input must release the attributes created before the source-reader failure.");
        });
    }

    [Test]
    public void Constructor_OtherInitializationFailure_StillLogsError()
    {
        string file = Path.Combine(_workDir, "missing.mp4");
        var logger = new CaptureLogger();
        int before = CountTrackedMediaFoundationObjects();

        SharpGenException? exception = Assert.Throws<SharpGenException>(() =>
            _ = new MFDecoder(file, new MFDecodingExtension(), logger));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.ResultCode.Code, Is.Not.EqualTo(unchecked((int)0xC00D36C4)));
            Assert.That(logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error }));
            Assert.That(logger.Entries[0].Exception, Is.SameAs(exception));
            Assert.That(CountTrackedMediaFoundationObjects(), Is.EqualTo(before));
        });
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
