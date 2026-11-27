using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Agents;

public sealed class WorkerAgentRuntimeBridge(string? workerDirectory = null, ILogger? logger = null) : IAgentRuntimeBridge
{
    private const string WorkerName = "Beutl.Agents.Worker";
    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly object _gate = new();
    private WorkerConnection? _worker;
    private Task? _disposal;

    public event Action<AgentTimelineEvent>? TimelineEventReceived;
    public event Action<Exception>? Disconnected;
    public Func<AgentPermissionRequest, CancellationToken, Task<AgentPermissionDecision>>? PermissionHandler { get; set; }
    public Func<AgentUserQuestionRequest, CancellationToken, Task<IReadOnlyDictionary<string, string[]>>>? QuestionHandler { get; set; }

    public async Task<AgentWorkerHealth> PingAsync(CancellationToken cancellationToken = default)
        => AgentJson.Read<AgentWorkerHealth>(
            await GetWorker(cancellationToken).Rpc.RequestAsync("ping", null, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<AgentModelDescriptor>> ListModelsAsync(
        AgentModelListRequest request, CancellationToken cancellationToken = default)
        => AgentJson.Read<AgentModelDescriptor[]>(
            await GetWorker(cancellationToken).Rpc.RequestAsync("models/list", AgentJson.ToElement(request), cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<AgentComposerSuggestion>> ListComposerSuggestionsAsync(
        AgentComposerSuggestionRequest request, CancellationToken cancellationToken = default)
        => AgentJson.Read<AgentComposerSuggestion[]>(
            await GetWorker(cancellationToken).Rpc.RequestAsync("suggestions/list", AgentJson.ToElement(request), cancellationToken).ConfigureAwait(false));

    public async Task<AgentSessionInfo> OpenSessionAsync(AgentSessionOpenRequest request, CancellationToken cancellationToken = default)
        => AgentJson.Read<AgentSessionInfo>(
            await GetWorker(cancellationToken).Rpc.RequestAsync("session/open", AgentJson.ToElement(request), cancellationToken).ConfigureAwait(false));

    public async Task<AgentTurnSummary> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var worker = GetRunningWorker() ?? throw new AgentWorkerDisconnectedException("Open the agent session before starting a turn.");
        var running = worker.Rpc.RequestAsync("turn/run", AgentJson.ToElement(request), CancellationToken.None);
        try
        {
            return AgentJson.Read<AgentTurnSummary>(await running.WaitAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var timeout = new CancellationTokenSource(s_shutdownTimeout);
            try
            {
                await worker.Rpc.RequestAsync("turn/cancel", AgentJson.ToElement(new { request.ThreadId, request.TurnId }), timeout.Token)
                    .ConfigureAwait(false);
                await running.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is AgentRpcException or IOException or OperationCanceledException)
            {
                if (timeout.IsCancellationRequested)
                    worker.Terminate();
                _logger.LogDebug(ex, "Agent turn stopped while waiting for cancellation.");
            }
            throw;
        }
    }

    public async Task<bool> SteerTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetRunningWorker() is not { } worker)
            return false;
        var result = await worker.Rpc.RequestAsync("turn/steer", AgentJson.ToElement(request), cancellationToken).ConfigureAwait(false);
        return result.GetProperty("steered").GetBoolean();
    }

    public Task CancelTurnAsync(Guid threadId, Guid turnId, CancellationToken cancellationToken = default)
        => RequestIfRunningAsync("turn/cancel", new { threadId, turnId }, cancellationToken);

    public async Task<AgentBackgroundStopResult> StopBackgroundTaskAsync(
        Guid threadId, Guid turnId, string toolCallId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetRunningWorker() is not { } worker)
            return AgentBackgroundStopResult.Unavailable;
        var result = await worker.Rpc.RequestAsync("turn/stopTask", AgentJson.ToElement(new { threadId, turnId, toolCallId }),
            cancellationToken).ConfigureAwait(false);
        return AgentJson.Read<AgentBackgroundStopResult>(result);
    }

    public Task SetPermissionModeAsync(Guid threadId, AgentPermissionMode mode, CancellationToken cancellationToken = default)
        => RequestIfRunningAsync("session/setMode", new { threadId, mode }, cancellationToken);

    public Task CloseSessionAsync(Guid threadId, CancellationToken cancellationToken = default)
        => RequestIfRunningAsync("session/close", new { threadId }, cancellationToken);

    private async Task RequestIfRunningAsync<T>(string method, T request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetRunningWorker() is { } worker)
            await worker.Rpc.RequestAsync(method, AgentJson.ToElement(request), cancellationToken).ConfigureAwait(false);
    }

    private WorkerConnection? GetRunningWorker()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal != null, this);
            return _worker is { Rpc.IsConnected: true } ? _worker : null;
        }
    }

    private WorkerConnection GetWorker(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (GetRunningWorker() is { } existing)
                return existing;

            var process = Process.Start(CreateStartInfo(workerDirectory, AppContext.BaseDirectory))
                ?? throw new InvalidOperationException("Could not start the agent worker.");
            SafeFileHandle? job = null;
            try
            {
                if (OperatingSystem.IsWindows())
                    job = AgentWorkerProcess.CreateJob(process);

                var rpc = new AgentJsonRpc(process.StandardOutput, process.StandardInput, HandleRequestAsync, OnNotification);
                var worker = new WorkerConnection(process, job, rpc);
                _worker = worker;
                rpc.Start();
                worker.Completion = ObserveAsync(worker);
                return worker;
            }
            catch
            {
                job?.Dispose();
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It already exited.
                }
                finally
                {
                    process.Dispose();
                }
                throw;
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string? directory, string applicationDirectory)
    {
        string location = Path.GetFullPath(directory ?? applicationDirectory);
        string executableName = OperatingSystem.IsWindows() ? WorkerName + ".exe" : WorkerName;
        if (directory is null)
        {
            string nested = Path.Combine(location, WorkerName);
            if (File.Exists(Path.Combine(nested, executableName)) || File.Exists(Path.Combine(nested, WorkerName + ".dll")))
                location = nested;
        }
        string executable = Path.Combine(location, executableName);
        string dll = Path.Combine(location, WorkerName + ".dll");

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var start = new ProcessStartInfo
        {
            WorkingDirectory = location,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = encoding,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (File.Exists(executable))
        {
            start.FileName = executable;
        }
        else if (File.Exists(dll))
        {
            start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            start.ArgumentList.Add(dll);
        }
        else
        {
            throw new FileNotFoundException($"Could not find {WorkerName} beside Beutl.", executable);
        }
        return start;
    }

    private async Task<JsonElement?> HandleRequestAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        // Host callbacks may synchronously dispatch to UI code. Keep the RPC reader free.
        await Task.Yield();
        switch (method)
        {
            case "agent/permission":
                var request = AgentJson.Read<AgentPermissionRequest>(parameters);
                var decision = PermissionHandler is { } permission
                    ? await permission(request, cancellationToken).ConfigureAwait(false)
                    : AgentPermissionDecision.RejectOnce;
                return AgentJson.ToElement(decision);
            case "agent/question":
                var question = AgentJson.Read<AgentUserQuestionRequest>(parameters);
                var answers = QuestionHandler is { } ask
                    ? await ask(question, cancellationToken).ConfigureAwait(false)
                    : new Dictionary<string, string[]>();
                return AgentJson.ToElement(answers);
            default:
                throw new AgentRpcException(-32601, $"Unknown host method {method}.");
        }
    }

    private void OnNotification(string method, JsonElement? parameters)
    {
        if (method != "agent/event")
            return;

        var entry = AgentJson.Read<AgentTimelineEvent>(parameters);
        try
        {
            TimelineEventReceived?.Invoke(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An agent timeline consumer failed.");
        }
    }

    private async Task ObserveAsync(WorkerConnection worker)
    {
        // Capture stderr immediately so the worker can always write diagnostics.
        Task stderr = ReadErrorsAsync(worker.Process.StandardError);
        await worker.Rpc.Completion.ConfigureAwait(false);
        if (worker.ExpectedExit)
        {
            try
            {
                using var timeout = new CancellationTokenSource(s_shutdownTimeout);
                await worker.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                _logger.LogDebug("Agent worker exited with code {ExitCode}.", worker.Process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                // Terminate below if the worker could not exit on its own.
            }
        }
        worker.Terminate();
        await stderr.ConfigureAwait(false);
        worker.Process.Dispose();

        lock (_gate)
        {
            if (ReferenceEquals(_worker, worker))
                _worker = null;
        }

        if (!worker.ExpectedExit)
        {
            try
            {
                Disconnected?.Invoke(worker.Rpc.DisconnectError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An agent worker disconnect consumer failed.");
            }
        }
    }

    private async Task ReadErrorsAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                _logger.LogDebug("Agent worker: {Line}", line);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "The agent worker diagnostic stream closed.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_worker is { } worker)
                worker.ExpectedExit = true;
            return new ValueTask(_disposal ??= ShutdownAsync(_worker));
        }
    }

    private async Task ShutdownAsync(WorkerConnection? worker)
    {
        if (worker == null)
            return;
        using var timeout = new CancellationTokenSource(s_shutdownTimeout);
        try
        {
            await worker.Rpc.RequestAsync("shutdown", null, timeout.Token).ConfigureAwait(false);
            worker.Process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is AgentRpcException or IOException or InvalidOperationException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "The agent worker did not acknowledge shutdown.");
            worker.Terminate();
        }
        await worker.Completion.ConfigureAwait(false);
    }

    private sealed class WorkerConnection(Process process, SafeFileHandle? job, AgentJsonRpc rpc)
    {
        private readonly int _processId = process.Id;
        private int _terminated;
        public Process Process { get; } = process;
        public AgentJsonRpc Rpc { get; } = rpc;
        public Task Completion { get; set; } = Task.CompletedTask;
        public volatile bool ExpectedExit;

        public void Terminate()
        {
            if (Interlocked.Exchange(ref _terminated, 1) != 0)
                return;

            job?.Dispose();
            AgentWorkerProcess.KillUnixGroup(_processId);
            try
            {
                if (!Process.HasExited)
                    Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // The worker exited while it was being reaped.
            }
        }
    }
}
