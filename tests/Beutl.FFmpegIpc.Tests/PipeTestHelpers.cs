using System.IO.Pipes;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>Connects named pipes and bounds the waits of the IPC tests.</summary>
internal static class PipeTestHelpers
{
    public static (NamedPipeServerStream Server, NamedPipeClientStream Client) ConnectPair(string namePrefix)
    {
        string name = namePrefix + Guid.NewGuid().ToString("N")[..8];
        var server = new NamedPipeServerStream(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

        var serverConnectTask = server.WaitForConnectionAsync();
        client.Connect(TimeSpan.FromSeconds(5));
        serverConnectTask.GetAwaiter().GetResult();
        return (server, client);
    }

    public static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        if (!predicate())
        {
            Assert.Fail($"Timeout {timeout.TotalMilliseconds}ms waiting for: {description}");
        }
    }

    /// <summary>
    /// Awaits <paramref name="task"/>, failing the test when it does not complete within <paramref name="timeout"/>.
    /// A task that faulted rethrows its exception.
    /// </summary>
    public static async Task AwaitWithTimeout(Task task, TimeSpan timeout, string description)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            Assert.Fail($"Timeout {timeout.TotalMilliseconds}ms waiting for: {description}");
        }
        await task;
    }

    /// <inheritdoc cref="AwaitWithTimeout(Task, TimeSpan, string)"/>
    public static async Task<T> AwaitWithTimeout<T>(Task<T> task, TimeSpan timeout, string description)
    {
        await AwaitWithTimeout((Task)task, timeout, description);
        return await task;
    }
}
