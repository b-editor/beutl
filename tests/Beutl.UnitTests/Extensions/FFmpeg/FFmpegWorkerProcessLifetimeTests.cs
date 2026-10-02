using System.IO.Pipes;
using Beutl.Extensions.FFmpeg;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture, NonParallelizable]
public class FFmpegWorkerProcessLifetimeTests
{
    [Test]
    public Task CancelingStartupTokenAfterSuccess_DoesNotStopSharedDecodingConnection()
    {
        return TestWorkerProgram.RunAsync(
            TestWorkerProgram.FFmpegLifetimeWorkerArgument, "--test", "cancel-after-startup");
    }

    [Test]
    public Task CancelingStartupTokenBeforeHandshake_CleansUpAndCanRetry()
    {
        return TestWorkerProgram.RunAsync(
            TestWorkerProgram.FFmpegLifetimeWorkerArgument, "--test", "cancel-before-handshake");
    }

    [Test]
    public Task ConcurrentStartup_WaitsForTheInitialHandshake()
    {
        return TestWorkerProgram.RunAsync(
            TestWorkerProgram.FFmpegLifetimeWorkerArgument, "--test", "concurrent-startup");
    }

    [Test]
    public Task AvailabilityObserverFailure_DoesNotCloseAnAlreadyPublishedConnection()
    {
        return TestWorkerProgram.RunAsync(
            TestWorkerProgram.FFmpegLifetimeWorkerArgument, "--test", "throwing-notification");
    }

    internal static async Task RunHostAsync(string action)
    {
        switch (action)
        {
            case "cancel-after-startup":
                await VerifyStartupTokenLifetimeAsync();
                break;
            case "cancel-before-handshake":
                await VerifyStartupCancellationAsync();
                break;
            case "concurrent-startup":
                await VerifyStartupReadinessAsync();
                break;
            case "throwing-notification":
                await VerifyObserverFailureAsync();
                break;
            default:
                throw new ArgumentException("Unknown worker lifetime test action.", nameof(action));
        }
    }

    private static async Task VerifyStartupTokenLifetimeAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var worker = CreateWorker();
        IpcConnection connection = await worker.EnsureStartedAsync(cancellation.Token);

        await cancellation.CancelAsync();

        var request = IpcMessage.CreateSimple(connection.NextId(), MessageType.CloseReader);
        IpcMessage response = await connection.SendAndReceiveAsync(request).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(response.Type, Is.EqualTo(MessageType.CloseReaderResult));
        Assert.That(await worker.EnsureStartedAsync(), Is.SameAs(connection));
    }

    private static async Task VerifyStartupCancellationAsync()
    {
        using var cancellation = new CancellationTokenSource();
        bool canceledOnce = false;
        using var worker = CreateWorker(_ =>
        {
            if (!canceledOnce)
            {
                canceledOnce = true;
                cancellation.Cancel();
            }
        });

        OperationCanceledException? error = await Assert.CatchAsync<OperationCanceledException>(async () =>
            await worker.EnsureStartedAsync(cancellation.Token));
        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(worker.IsRunning, Is.False);
            Assert.That(worker.WorkerPid, Is.Zero);
        });

        IpcConnection connection = await worker.EnsureStartedAsync();
        var request = IpcMessage.CreateSimple(connection.NextId(), MessageType.CloseReader);
        IpcMessage response = await connection.SendAndReceiveAsync(request).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(response.Type, Is.EqualTo(MessageType.CloseReaderResult));
    }

    private static async Task VerifyStartupReadinessAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string gateName = $"beutl-handshake-test-{Guid.NewGuid():N}";
        using var gate = new NamedPipeServerStream(
            gateName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var worker = CreateWorker(start =>
        {
            start.ArgumentList.Add("--handshake-gate");
            start.ArgumentList.Add(gateName);
        });

        FFmpegLibraryState.RecordMissingObserved();
        IpcConnection? notificationConnection = null;
        EventHandler onAvailability = (_, _) => notificationConnection = worker.EnsureStarted();
        FFmpegLibraryState.AvailabilityChanged += onAvailability;
        Task<IpcConnection> first = worker.EnsureStartedAsync(timeout.Token);
        await gate.WaitForConnectionAsync(timeout.Token);
        var connectionField = typeof(FFmpegWorkerProcess).GetField("_connection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        // Wait for the host to reach the handshake receive, while the worker holds its acknowledgement.
        while (connectionField.GetValue(worker) == null)
            await Task.Delay(10, timeout.Token);

        Task<IpcConnection> second = worker.EnsureStartedAsync(timeout.Token);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False,
                    "A connected pipe must not be returned to another caller before its handshake completes.");
                Assert.That(worker.IsRunning, Is.False,
                    "The worker becomes ready after the handshake, not after the pipe connects.");
            });
        }
        finally
        {
            try
            {
                await gate.WriteAsync(new byte[] { 1 }, timeout.Token);
                await Task.WhenAll(first, second).WaitAsync(timeout.Token);
            }
            finally
            {
                FFmpegLibraryState.AvailabilityChanged -= onAvailability;
            }
        }

        Assert.That(await second, Is.SameAs(await first));
        Assert.That(notificationConnection, Is.SameAs(await first),
            "The ready notification can re-enter startup without waiting on its own startup lock.");
        Assert.That(worker.IsRunning, Is.True);
    }

    private static async Task VerifyObserverFailureAsync()
    {
        using var worker = CreateWorker();
        using var releaseNotification = new ManualResetEventSlim();
        var notificationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observerError = new InvalidOperationException("Injected availability observer failure.");

        FFmpegLibraryState.RecordMissingObserved();
        EventHandler onAvailability = (_, _) =>
        {
            notificationEntered.SetResult();
            if (!releaseNotification.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the availability notification.");
            throw observerError;
        };
        FFmpegLibraryState.AvailabilityChanged += onAvailability;
        Task<IpcConnection> first = worker.EnsureStartedAsync();
        try
        {
            await notificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            IpcConnection connection = await worker.EnsureStartedAsync().WaitAsync(TimeSpan.FromSeconds(10));

            releaseNotification.Set();
            InvalidOperationException? error = await Assert.CatchAsync<InvalidOperationException>(async () =>
                await first.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(error, Is.SameAs(observerError), "The observer exception still reaches its initiating caller.");
            Assert.That(worker.IsRunning, Is.True,
                "A callback failure must not roll back a connection already returned to another caller.");
            Assert.That(await worker.EnsureStartedAsync(), Is.SameAs(connection));

            var request = IpcMessage.CreateSimple(connection.NextId(), MessageType.CloseReader);
            IpcMessage response = await connection.SendAndReceiveAsync(request).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(response.Type, Is.EqualTo(MessageType.CloseReaderResult));
        }
        finally
        {
            releaseNotification.Set();
            FFmpegLibraryState.AvailabilityChanged -= onAvailability;
            try { await first.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception) { }
        }
    }

    private static FFmpegWorkerProcess CreateWorker(Action<System.Diagnostics.ProcessStartInfo>? afterConfigure = null) => new(true, start =>
    {
        start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(TestWorkerProgram).Assembly.Location);
        start.ArgumentList.Add(TestWorkerProgram.FFmpegLifetimeWorkerArgument);
        afterConfigure?.Invoke(start);
    });

    internal static async Task RunWorkerAsync(string[] arguments)
    {
        int pipeArgument = Array.IndexOf(arguments, "--pipe");
        if (pipeArgument < 0 || pipeArgument + 1 >= arguments.Length)
            throw new ArgumentException("Missing pipe argument.", nameof(arguments));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pipe = new NamedPipeClientStream(
            ".", arguments[pipeArgument + 1], PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        int gateArgument = Array.IndexOf(arguments, "--handshake-gate");
        if (gateArgument >= 0)
        {
            using var gate = new NamedPipeClientStream(
                ".", arguments[gateArgument + 1], PipeDirection.InOut, PipeOptions.Asynchronous);
            await gate.ConnectAsync(timeout.Token);
            await gate.ReadExactlyAsync(new byte[1], timeout.Token);
        }

        await MessageSerializer.WriteMessageAsync(pipe,
            IpcMessage.Create(0, MessageType.HandshakeAck, new HandshakeMessage()), timeout.Token);

        while (await MessageSerializer.ReadMessageAsync(pipe, timeout.Token) is { } request)
        {
            if (request.Type == MessageType.Shutdown)
                return;

            await MessageSerializer.WriteMessageAsync(pipe,
                IpcMessage.CreateSimple(request.Id, MessageType.CloseReaderResult), timeout.Token);
        }
    }
}
