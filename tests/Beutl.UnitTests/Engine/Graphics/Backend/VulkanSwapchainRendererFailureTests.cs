using System.Collections.Concurrent;
using System.Reflection;
using Beutl.Configuration;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// How the presentation thread reacts to what Vulkan returns, with the device replaced so that a call can fail on
// demand (#2696).
[TestFixture]
public class VulkanSwapchainRendererFailureTests
{
    private static readonly RenderParams s_params =
        new(16, 9, Stretch.Uniform, UIToneMappingOperator.None, 0f, IsSourceLinear: true);

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private FakePresentCalls _calls = null!;
    private CapturingLoggerProvider _logs = null!;
    private VulkanSwapchainRenderer _renderer = null!;

    [SetUp]
    public void SetUp()
    {
        _calls = new FakePresentCalls();
        _logs = new CapturingLoggerProvider(_ => true);
        _renderer = new VulkanSwapchainRenderer(_calls, _logs.CreateLogger(nameof(VulkanSwapchainRenderer)));
    }

    [TearDown]
    public void TearDown()
    {
        _renderer.Dispose();
        _logs.Dispose();
    }

    // A failed submit leaves the fence unsignaled, so waiting on it again would block every later frame.
    [Test]
    public void AfterAFailedSubmitLaterDrawsAndResizesMakeNoVulkanCalls()
    {
        _calls.SubmitResults.Enqueue(Result.ErrorDeviceLost);
        Draw();
        Assert.That(_calls.Count("QueueSubmit"), Is.EqualTo(1));
        int callsAtFault = _calls.Calls.Count;

        Draw();
        // The surface takes another size, which a healthy renderer follows before drawing.
        _calls.SurfaceExtent = new Extent2D(128, 72);
        Draw();
        ResizeAndWait(128, 72);
        _renderer.RequestRedraw(s_params);
        Draw();

        Assert.That(_calls.Calls.Skip(callsAtFault), Is.Empty);
        AssertNoUnexpectedErrors();
    }

    [Test]
    public void AFrameWhoseSubmitFailedIsReleased()
    {
        _calls.SubmitResults.Enqueue(Result.ErrorDeviceLost);

        using var released = new ManualResetEventSlim();
        _renderer.RequestRender(NewFrame(released.Set), s_params);

        Assert.That(released.Wait(s_timeout), Is.True, "The frame whose submit failed was not released.");
        Assert.That(_calls.Count("UploadBitmap"), Is.EqualTo(1));
        Assert.That(_calls.Count("QueueSubmit"), Is.EqualTo(1));
        Assert.That(_calls.Count("QueuePresent"), Is.Zero);
    }

    [Test]
    public void AFailedSubmitIsLoggedOnce()
    {
        _calls.SubmitResults.Enqueue(Result.ErrorDeviceLost);
        Draw();
        Draw();
        ResizeAndWait(32, 18);
        _renderer.RequestRedraw(s_params);
        Draw();

        var errors = _logs.Entries.Where(e => e.Level >= LogLevel.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Exception?.Message, Does.Contain("vkQueueSubmit").And.Contain(nameof(Result.ErrorDeviceLost)));
    }

    // Acquiring an image of a swapchain the surface outgrew rebuilds it, and the frame is drawn into the new one.
    [Test]
    public void AnOutOfDateAcquireRecreatesTheSwapchainAndPresentsTheFrame()
    {
        _calls.AcquireResults.Enqueue(Result.ErrorOutOfDateKhr);
        Draw();

        Assert.That(_calls.Count("RecreateSwapchain"), Is.EqualTo(1));
        Assert.That(_calls.Count("QueuePresent"), Is.EqualTo(1));

        AssertPresentationContinues();
    }

    // An out-of-date present showed nothing, so the frame is drawn again into the rebuilt swapchain; a suboptimal one was
    // shown, so the swapchain is only rebuilt.
    [TestCase(Result.ErrorOutOfDateKhr, 2)]
    [TestCase(Result.SuboptimalKhr, 1)]
    public void AnOutOfDateOrSuboptimalPresentRecreatesTheSwapchain(Result presentResult, int presents)
    {
        _calls.PresentResults.Enqueue(presentResult);
        Draw();

        Assert.That(_calls.Count("RecreateSwapchain"), Is.EqualTo(1));
        Assert.That(_calls.Count("QueuePresent"), Is.EqualTo(presents));

        AssertPresentationContinues();
    }

    // A suboptimal image can still be drawn into: the frame is presented, and the present's own result decides whether
    // the swapchain is rebuilt.
    [Test]
    public void ASuboptimalAcquireStillPresentsTheFrame()
    {
        _calls.AcquireResults.Enqueue(Result.SuboptimalKhr);
        Draw();

        Assert.That(_calls.Count("QueuePresent"), Is.EqualTo(1));

        AssertPresentationContinues();
    }

    // A GPU that has not finished the previous frame in time costs only the frame waiting on it.
    [Test]
    public void AFenceWaitTimeoutDropsOnlyThatFrame()
    {
        _calls.WaitResults.Enqueue(Result.Timeout);
        Draw();

        Assert.That(_calls.Count("WaitForFences"), Is.EqualTo(1));
        Assert.That(_calls.Count("UploadBitmap"), Is.Zero);
        Assert.That(_calls.Count("QueueSubmit"), Is.Zero);
        Assert.That(_logs.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(1));

        AssertPresentationContinues();
    }

    // The next frame is uploaded, submitted and presented, and nothing was logged as an error.
    private void AssertPresentationContinues()
    {
        int uploads = _calls.Count("UploadBitmap");
        int submits = _calls.Count("QueueSubmit");
        int presents = _calls.Count("QueuePresent");

        Draw();

        Assert.That(_calls.Count("UploadBitmap"), Is.EqualTo(uploads + 1));
        Assert.That(_calls.Count("QueueSubmit"), Is.EqualTo(submits + 1));
        Assert.That(_calls.Count("QueuePresent"), Is.EqualTo(presents + 1));
        AssertNoUnexpectedErrors();
    }

    // Only a failed Vulkan call is logged as an error: anything else is the renderer reaching past the fake.
    private void AssertNoUnexpectedErrors()
    {
        Assert.That(
            _logs.Entries.Where(e => e.Level >= LogLevel.Error && e.Exception?.Message.Contains("failed with") != true),
            Is.Empty);
    }

    // Requests a frame and waits until the presentation thread has released it, which it does once the batch is done.
    private void Draw()
    {
        using var released = new ManualResetEventSlim();
        _renderer.RequestRender(NewFrame(released.Set), s_params);
        Assert.That(released.Wait(s_timeout), Is.True, "The frame was not released.");
    }

    // Requests a resize and waits until the presentation thread has taken it alone, so no frame joins its batch.
    private void ResizeAndWait(uint width, uint height)
    {
        _renderer.Resize(width, height);
        object commandQueue = typeof(VulkanSwapchainRenderer)
            .GetField("_commandQueue", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_renderer)!;
        PropertyInfo count = commandQueue.GetType().GetProperty("Count")!;
        Assert.That(SpinWait.SpinUntil(() => (int)count.GetValue(commandQueue)! == 0, s_timeout), Is.True,
            "The resize was not taken.");
    }

    private static Ref<Bitmap> NewFrame(Action onRelease)
    {
        return Ref<Bitmap>.Create(
            new Bitmap(16, 9, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb),
            onRelease);
    }

    // Records each call, and returns the results queued for it before Success.
    private sealed class FakePresentCalls : IVulkanPresentCalls
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private volatile bool _hasSourceImage;

        public ConcurrentQueue<Result> WaitResults { get; } = new();

        public ConcurrentQueue<Result> AcquireResults { get; } = new();

        public ConcurrentQueue<Result> SubmitResults { get; } = new();

        public ConcurrentQueue<Result> PresentResults { get; } = new();

        public IReadOnlyList<string> Calls => _calls.ToArray();

        public Extent2D? SurfaceExtent { get; set; } = new Extent2D(64, 36);

        public Extent2D SwapchainExtent { get; private set; } = new(64, 36);

        public bool HasSourceImage => _hasSourceImage;

        public int Count(string call) => _calls.Count(c => c == call);

        public Extent2D? GetSurfaceExtent()
        {
            _calls.Enqueue(nameof(GetSurfaceExtent));
            return SurfaceExtent;
        }

        public Result WaitForFences(ulong timeoutNanoseconds) => Next(nameof(WaitForFences), WaitResults);

        public Result ResetFences() => Next(nameof(ResetFences), null);

        public Result DeviceWaitIdle() => Next(nameof(DeviceWaitIdle), null);

        public void RecreateSwapchain(uint width, uint height)
        {
            _calls.Enqueue(nameof(RecreateSwapchain));
            SwapchainExtent = new Extent2D(width, height);
        }

        public void UploadBitmap(Bitmap bitmap)
        {
            _calls.Enqueue(nameof(UploadBitmap));
            _hasSourceImage = true;
        }

        public void UploadOverlay() => _calls.Enqueue(nameof(UploadOverlay));

        public Result AcquireNextImage(out uint imageIndex)
        {
            imageIndex = 0;
            return Next(nameof(AcquireNextImage), AcquireResults);
        }

        public void RecordCommands(uint imageIndex, RenderParams renderParams) => _calls.Enqueue(nameof(RecordCommands));

        public Result QueueSubmit() => Next(nameof(QueueSubmit), SubmitResults);

        public Result QueuePresent(uint imageIndex) => Next(nameof(QueuePresent), PresentResults);

        private Result Next(string call, ConcurrentQueue<Result>? results)
        {
            _calls.Enqueue(call);
            return results != null && results.TryDequeue(out Result result) ? result : Result.Success;
        }
    }
}
