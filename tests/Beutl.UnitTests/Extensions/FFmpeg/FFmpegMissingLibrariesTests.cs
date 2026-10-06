using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;
using Beutl.FFmpegIpc;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Moq;
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
[NonParallelizable]
public sealed class FFmpegMissingLibrariesTests
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly CaptureLoggerProvider _logs = new();
    private HeadlessUnitTestSession _session = null!;

    [OneTimeSetUp]
    public void Initialize()
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        Log.LoggerFactory.AddProvider(_logs);
        _session = HeadlessUnitTestSession.StartNew(typeof(Application));
    }

    [SetUp]
    public void SetUp()
    {
        FFmpegOptionsCaches.ClearAll();
        FFmpegWorkerCodecCache.Invalidate();
        FFmpegInstallNotifier.MarkInstalled();
        _logs.Entries.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        FFmpegOptionsCaches.ClearAll();
        FFmpegWorkerCodecCache.Invalidate();
        FFmpegInstallNotifier.MarkInstalled();
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        _session.Dispose();
        _logs.Dispose();
    }

    [TestCase("audio", true)]
    [TestCase("audio", false)]
    [TestCase("pixel", true)]
    [TestCase("pixel", false)]
    [TestCase("sample-rate", true)]
    [TestCase("sample-rate", false)]
    public async Task EditorRefresh_PreservesFallbackAndLogsOnlyUnexpectedFailures(string editorKind, bool missing)
    {
        Exception failure = missing
            ? new FFmpegLibrariesNotFoundException("FFmpeg is not installed.")
            : new InvalidOperationException("Worker query failed.");

        await _session.Dispatch(async () =>
        {
            switch (editorKind)
            {
                case "audio":
                    var audioProperty = CreateProperty(AudioFormat.Fltp);
                    using (var editor = new AudioFormatEditorViewModel(audioProperty.Object, PropertyEditorExtension.Instance))
                    {
                        await FailRefreshAsync(editor, FFmpegOptionsCaches.AudioFormats, failure);
                        Assert.That(GetField<AudioFormat[]>(editor, "_currentFormats"),
                            Is.EqualTo(new[] { AudioFormat.Default }.Concat(AudioFormatOptions.All())));
                        audioProperty.Verify(property => property.SetValue(It.IsAny<AudioFormat>()), Times.Never);
                    }
                    break;
                case "pixel":
                    var pixelProperty = CreateProperty(FFPixelFormat.YUV420P);
                    using (var editor = new PixelFormatEditorViewModel(pixelProperty.Object, PropertyEditorExtension.Instance))
                    {
                        await FailRefreshAsync(editor, FFmpegOptionsCaches.PixelFormats, failure);
                        Assert.That(GetField<int[]>(editor, "_currentFormats"), Is.EqualTo(new[] { FFPixelFormat.None }));
                        pixelProperty.Verify(property => property.SetValue(It.IsAny<int>()), Times.Never);
                    }
                    break;
                default:
                    var rateProperty = CreateProperty(48000);
                    using (var editor = new SampleRateEditorViewModel(rateProperty.Object, PropertyEditorExtension.Instance))
                    {
                        await FailRefreshAsync(editor, FFmpegOptionsCaches.SampleRates, failure);
                        Assert.That(GetField<string[]>(editor, "_currentSuggestions"), Is.Empty);
                        rateProperty.Verify(property => property.SetValue(It.IsAny<int>()), Times.Never);
                    }
                    break;
            }

            return 0;
        }, CancellationToken.None);

        if (missing)
            Assert.That(_logs.Entries, Is.Empty, "An expected installation state must not be logged.");
        else
            Assert.That(_logs.Entries.ToArray(), Is.EqualTo(new[] { (LogLevel.Warning, failure) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CodecChoices_WhenLibrariesMissing_ReturnDefaultWithoutLogging(bool audio)
    {
        if (FFmpegWorkerProcess.DecodingInstance.IsRunning)
            Assert.Ignore("A shared decoder is already running; the missing-library path cannot be exercised.");

        FFmpegInstallNotifier.MarkMissing();

        IReadOnlyList<object> codecs = audio
            ? FFmpegWorkerCodecCache.GetAudioCodecs()
            : FFmpegWorkerCodecCache.GetVideoCodecs();

        Assert.That(codecs, Is.EqualTo(new object[] { CodecRecord.Default }));
        Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.True);
        Assert.That(_logs.Entries, Is.Empty);
    }

    [Test]
    public void DecoderLoad_WhenLibrariesMissing_OffersInstallationWithoutLogging()
    {
        if (FFmpegWorkerProcess.DecodingInstance.IsRunning)
            Assert.Ignore("A shared decoder is already running; the missing-library path cannot be exercised.");

        var notifications = new Mock<INotificationServiceHandler>();
        Notification? shown = null;
        notifications.Setup(handler => handler.Show(It.IsAny<Notification>()))
            .Callback<Notification>(notification => shown = notification);
        FieldInfo handlerField = typeof(NotificationService).GetField("s_handler", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previousHandler = handlerField.GetValue(null);
        var extension = new FFmpegDecodingExtension();
        FFmpegInstallNotifier.MarkMissing();
        NotificationService.Handler = notifications.Object;
        try
        {
            extension.Load();

            Assert.That(shown, Is.Not.Null);
            Assert.That(shown!.Actions, Has.Count.EqualTo(1));
            Assert.That(shown.Actions![0].Text, Is.EqualTo(Beutl.Extensions.FFmpeg.Properties.Strings.Install));
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.True);
            Assert.That(_logs.Entries, Is.Empty);
        }
        finally
        {
            extension.Unload();
            handlerField.SetValue(null, previousHandler);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task InstallationVerification_ReportsFailureAndLogsOnlyUnexpectedFailures(bool missing)
    {
        var worker = FFmpegWorkerProcess.DecodingInstance;
        if (worker.IsRunning)
            Assert.Ignore("A shared decoder is already running; verification would reuse its connection.");

        FieldInfo configureField = typeof(FFmpegWorkerProcess).GetField("_configureWorkerStart", InstanceFlags)!;
        FieldInfo failureField = typeof(FFmpegWorkerProcess).GetField("_lastStartupFailure", InstanceFlags)!;
        FieldInfo retryField = typeof(FFmpegWorkerProcess).GetField("_retryStartupAt", InstanceFlags)!;
        object? previousConfigure = configureField.GetValue(worker);
        object? previousFailure = failureField.GetValue(worker);
        object? previousRetry = retryField.GetValue(worker);
        Exception failure = missing
            ? new FFmpegLibrariesNotFoundException("FFmpeg is not installed.")
            : new InvalidOperationException("Worker startup failed.");
        var installer = new FFmpegInstallService();
        bool? completed = null;
        var indeterminate = new List<bool>();
        installer.Completed += success => completed = success;
        installer.IndeterminateChanged += indeterminate.Add;
        try
        {
            // Fail before launching a process, independent of installed libraries and the worker executable.
            configureField.SetValue(worker, (Action<ProcessStartInfo>)(_ => throw failure));
            failureField.SetValue(worker, null);
            retryField.SetValue(worker, 0L);
            var verification = (Task)typeof(FFmpegInstallService).GetMethod("VerifyAndCompleteAsync", InstanceFlags)!
                .Invoke(installer, null)!;
            await verification.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(completed, Is.False);
            Assert.That(indeterminate, Is.EqualTo(new[] { true, false }));
            Assert.That(FFmpegInstallNotifier.IsLibrariesMissing, Is.True);
            Assert.That(FFmpegInstallNotifier.IsVerificationInProgress, Is.False);
            if (missing)
                Assert.That(_logs.Entries, Is.Empty);
            else
                Assert.That(_logs.Entries.ToArray(), Is.EqualTo(new[] { (LogLevel.Error, failure) }));
        }
        finally
        {
            configureField.SetValue(worker, previousConfigure);
            failureField.SetValue(worker, previousFailure);
            retryField.SetValue(worker, previousRetry);
        }
    }

    private static Mock<IPropertyAdapter<T>> CreateProperty<T>(T value)
    {
        var property = new Mock<IPropertyAdapter<T>>();
        property.Setup(adapter => adapter.GetValue()).Returns(value);
        property.Setup(adapter => adapter.GetObservable()).Returns(Observable.Return(value));
        return property;
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, InstanceFlags)!.GetValue(instance)!;

    private static async Task FailRefreshAsync<T>(IPropertyEditorContext editor, FFmpegOptionsCache<T> cache, Exception failure)
    {
        var query = new CodecQueryParams("test-codec", "output.mp4");
        string key = CodecOptionQuery.BuildCacheKey(query);
        var completion = new TaskCompletionSource<OptionsQueryResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Share an in-flight query with the editor so failures are deterministic without starting a worker.
        Task<OptionsQueryResult<T>> pending = cache.GetOrQueryAsync(key, () => completion.Task);
        var refresh = (Task)editor.GetType().GetMethod("UpdateAsync", InstanceFlags)!
            .Invoke(editor, [query, key, CancellationToken.None])!;
        completion.SetException(failure);
        await refresh.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(await Assert.CatchAsync<Exception>(async () => await pending), Is.SameAs(failure));
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        private bool _disposed;
        public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

        public void Dispose() => _disposed = true;

        private sealed class CaptureLogger(CaptureLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => !provider._disposed;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && category.StartsWith("Beutl.Extensions.FFmpeg", StringComparison.Ordinal))
                    provider.Entries.Enqueue((logLevel, exception));
            }
        }
    }
}
