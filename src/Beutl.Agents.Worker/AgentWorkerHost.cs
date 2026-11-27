using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Beutl.Agents.Worker;

internal sealed class AgentWorkerHost
{
    private readonly AgentJsonRpc _rpc;
    private readonly Lazy<Task<EmbeddedNode>> _node;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _hostRequests = new();
    private Task? _shutdown;

    internal AgentWorkerHost(TextReader input, TextWriter output)
    {
        _rpc = new AgentJsonRpc(input, output, CommandAsync);
        _node = new Lazy<Task<EmbeddedNode>>(() =>
            Task.Run(() => EmbeddedNode.StartAsync(
                json => _rpc.Notify("agent/event", JsonSerializer.Deserialize<JsonElement>(json)),
                RequestHostAsync)));
    }

    internal async Task RunAsync()
    {
        _rpc.Start();
        try
        {
            await _rpc.Completion.ConfigureAwait(false);
        }
        finally
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
    }

    private async Task<JsonElement?> CommandAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (method == "shutdown")
        {
            await ShutdownAsync().ConfigureAwait(false);
            return null;
        }
        if (method is not ("ping" or "models/list" or "suggestions/list" or "session/open" or "session/close" or "session/setMode" or "turn/run" or "turn/steer" or "turn/cancel"))
            throw new AgentRpcException(-32601, $"Unknown worker method {method}.");

        Task<EmbeddedNode> start;
        lock (_gate)
        {
            if (_shutdown != null)
                throw new AgentRpcException(-32000, "The agent worker is shutting down.");
            start = _node.Value;
        }

        var node = await start.WaitAsync(cancellationToken).ConfigureAwait(false);
        var command = AgentJson.ToElement(new { method, @params = parameters });
        var running = node.InvokeAsync(command);
        try
        {
            return await running.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (method == "session/open")
        {
            var threadId = parameters!.Value.GetProperty("threadId");
            await node.InvokeAsync(AgentJson.ToElement(new { method = "session/close", @params = new { threadId } }))
                .ConfigureAwait(false);
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
            throw;
        }
    }

    private async Task<string> RequestHostAsync(string json)
    {
        var request = JsonSerializer.Deserialize<JsonElement>(json);
        string method = request.GetProperty("method").GetString()!;
        string id = request.GetProperty("id").GetString()!;
        if (method == "request/cancel")
        {
            if (_hostRequests.TryGetValue(id, out var pending))
            {
                try
                {
                    await pending.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // The host responded first.
                }
            }
            return "null";
        }

        var parameters = request.GetProperty("params");
        if (method == "process/terminate")
        {
            KillChild(parameters.GetProperty("pid").GetInt32());
            return "null";
        }
        if (method is not ("agent/permission" or "agent/question"))
            throw new InvalidOperationException($"Unknown Node host method {method}.");

        using var cancellation = new CancellationTokenSource();
        _hostRequests[id] = cancellation;
        try
        {
            var response = await _rpc.RequestAsync(method, parameters, cancellation.Token).ConfigureAwait(false);
            return response.GetRawText();
        }
        finally
        {
            _hostRequests.TryRemove(id, out _);
        }
    }

    private static void KillChild(int pid)
    {
        try
        {
            using var child = Process.GetProcessById(pid);
            child.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The provider may have already exited.
        }
    }

    private Task ShutdownAsync()
    {
        lock (_gate)
            return _shutdown ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        if (!_node.IsValueCreated)
            return;

        EmbeddedNode node;
        try
        {
            node = await _node.Value.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return;
        }

        using (node)
        {
            await node.InvokeAsync(AgentJson.ToElement(new { method = "shutdown" })).ConfigureAwait(false);
        }
    }
}
