using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.OutputTab;

public sealed class OutputProfileItem : IDisposable, IOutputExecutionController
{
    private readonly ILogger<OutputProfileItem> _logger = Log.CreateLogger<OutputProfileItem>();
    private readonly IOutputRunHost _runHost;
    private readonly object _outputOperationSync = new();
    private readonly ReactivePropertySlim<bool> _isRunning = new();
    private CancellationTokenSource? _executionCancellation;
    private Task? _executionTask;
    private bool _disposeRequested;
    private bool _contextDisposed;

    internal OutputProfileItem(IOutputContext context, IEditorContext editorContext, IOutputRunHost runHost)
    {
        Context = context;
        EditorContext = editorContext;
        _runHost = runHost;

        _logger.LogInformation("OutputProfileItem created. File: {File}, Context: {Context}", Context.Object.Uri,
            Context);
    }

    public IOutputContext Context { get; }

    public IEditorContext EditorContext { get; }

    public IReadOnlyReactiveProperty<bool> IsRunning => _isRunning;

    public bool TryStart([NotNullWhen(true)] out Task? execution)
    {
        IDisposable? outputOperation;
        CancellationTokenSource? executionCancellation = null;
        TaskCompletionSource? completion = null;
        lock (_outputOperationSync)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested || _contextDisposed, this);
            if (_executionTask is not null)
            {
                execution = _executionTask;
                return true;
            }

            outputOperation = _runHost.TryBeginOutputOperation();
            if (outputOperation is not null)
            {
                executionCancellation = new CancellationTokenSource();
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _executionCancellation = executionCancellation;
                _executionTask = completion.Task;
            }
        }

        if (outputOperation is null)
        {
            _logger.LogWarning(
                "Could not reserve the workspace before starting output for {File}.",
                Context.Object.Uri);
            NotificationService.ShowWarning(
                Strings.Output,
                Strings.Output_WorkspaceBusy);
            execution = null;
            return false;
        }

        _ = RunOutputAsync(
            outputOperation,
            executionCancellation!,
            completion!);
        execution = completion!.Task;
        return true;
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_outputOperationSync)
        {
            if (_executionTask is null)
            {
                return;
            }

            cancellation = _executionCancellation;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An output cancellation callback failed.");
        }
    }

    private async Task RunOutputAsync(
        IDisposable outputOperation,
        CancellationTokenSource executionCancellation,
        TaskCompletionSource completion)
    {
        bool canceled = false;
        CancellationToken executionToken = executionCancellation.Token;
        List<Exception>? failures = null;
        IDisposable? runningOutput = null;
        try
        {
            _isRunning.Value = true;
            runningOutput = _runHost.TrackRunningOutput(this);

            executionToken.ThrowIfCancellationRequested();
            await Context.RunAsync(executionToken);
        }
        catch (OperationCanceledException)
            when (executionCancellation.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            failures = [ex];
        }
        finally
        {
            if (runningOutput is not null)
            {
                CaptureCleanupFailure(runningOutput.Dispose, ref failures);
            }

            CaptureCleanupFailure(executionCancellation.Dispose, ref failures);

            lock (_outputOperationSync)
            {
                if (ReferenceEquals(_executionCancellation, executionCancellation))
                {
                    _executionCancellation = null;
                }
            }

            CompleteExecution(
                completion,
                outputOperation,
                failures,
                canceled,
                executionToken);
        }
    }

    private void CompleteExecution(
        TaskCompletionSource completion,
        IDisposable outputOperation,
        List<Exception>? failures,
        bool canceled,
        CancellationToken cancellationToken)
    {
        bool disposeRunningProperty = false;
        while (true)
        {
            bool disposeContext;
            lock (_outputOperationSync)
            {
                disposeContext = TryClaimDeferredContextDisposal_NoLock(completion);
                if (!disposeContext)
                {
                    // Keep admission until any deferred context disposal has finished. The terminal
                    // task is published before releasing the lease so a newly admitted workspace
                    // mutation can never overlap an execution that still appears incomplete.
                    CaptureCleanupFailure(() => _isRunning.Value = false, ref failures);
                    disposeContext = TryClaimDeferredContextDisposal_NoLock(completion);
                    if (!disposeContext)
                    {
                        if (disposeRunningProperty)
                        {
                            CaptureCleanupFailure(_isRunning.Dispose, ref failures);
                        }

                        // Publish the terminal result before clearing the single-flight task. A
                        // concurrent start must either join this execution or observe it as already
                        // complete; it must never enter the context between those two state changes.
                        PublishTerminalResult(completion, failures, canceled, cancellationToken);
                        ReleaseOutputOperation_NoLock(outputOperation);
                        if (ReferenceEquals(_executionTask, completion.Task))
                        {
                            _executionTask = null;
                        }

                        return;
                    }
                }
            }

            if (disposeContext)
            {
                CaptureCleanupFailure(DisposeOutputContext, ref failures);
                disposeRunningProperty = true;
            }
        }
    }

    // A Dispose that arrived while this execution ran left the context to it; the first caller to
    // see that takes the disposal over.
    private bool TryClaimDeferredContextDisposal_NoLock(TaskCompletionSource completion)
    {
        if (_disposeRequested
            && !_contextDisposed
            && ReferenceEquals(_executionTask, completion.Task))
        {
            _contextDisposed = true;
            return true;
        }

        return false;
    }

    private static void PublishTerminalResult(
        TaskCompletionSource completion,
        List<Exception>? failures,
        bool canceled,
        CancellationToken cancellationToken)
    {
        if (failures is null)
        {
            if (canceled)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                completion.TrySetResult();
            }
        }
        else if (failures.Count == 1)
        {
            completion.TrySetException(failures[0]);
        }
        else
        {
            completion.TrySetException(new AggregateException(failures));
        }
    }

    private void ReleaseOutputOperation_NoLock(IDisposable outputOperation)
    {
        try
        {
            outputOperation.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The output workspace lease failed during terminal release.");
        }
    }

    private static void CaptureCleanupFailure(Action cleanup, ref List<Exception>? failures)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }
    }

    public void Dispose()
    {
        bool disposeContext;
        lock (_outputOperationSync)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
            disposeContext = TryMarkContextDisposed();
        }

        if (disposeContext)
        {
            DisposeContext();
        }
    }

    internal bool TryClaimDisposalIfIdle()
    {
        lock (_outputOperationSync)
        {
            if (_disposeRequested)
            {
                return _contextDisposed;
            }

            if (_executionTask is not null)
            {
                return false;
            }

            _disposeRequested = true;
            return true;
        }
    }

    internal void CompleteClaimedDisposal()
    {
        bool disposeContext;
        lock (_outputOperationSync)
        {
            if (!_disposeRequested)
            {
                throw new InvalidOperationException("Output profile disposal was not claimed.");
            }

            disposeContext = TryMarkContextDisposed();
        }

        if (disposeContext)
        {
            DisposeContext();
        }
    }

    private bool TryMarkContextDisposed()
    {
        if (!_disposeRequested
            || _contextDisposed
            || _executionTask is not null)
        {
            return false;
        }

        _contextDisposed = true;
        return true;
    }

    private void DisposeContext()
    {
        Exception? contextFailure = null;
        try
        {
            DisposeOutputContext();
        }
        catch (Exception ex)
        {
            contextFailure = ex;
        }

        try
        {
            _isRunning.Dispose();
        }
        catch (Exception ex) when (contextFailure is not null)
        {
            throw new AggregateException(contextFailure, ex);
        }

        if (contextFailure is not null)
        {
            ExceptionDispatchInfo.Capture(contextFailure).Throw();
        }
    }

    private void DisposeOutputContext()
    {
        _logger.LogInformation("Disposing OutputProfileItem for file: {File}", Context.Object.Uri);
        Context.Dispose();
    }

    public static JsonNode ToJson(OutputProfileItem item)
    {
        var ctxJson = new JsonObject();
        item.Context.WriteToJson(ctxJson);
        return new JsonObject
        {
            ["Extension"] = TypeFormat.ToString(item.Context.Extension.GetType()),
            ["ObjectId"] = item.Context.Object.Id.Value,
            [nameof(Context)] = ctxJson
        };
    }

    internal static OutputProfileItem? FromJson(IEditorContext editorContext, JsonNode json, ILogger logger,
        ExtensionProvider extensionProvider, IOutputRunHost runHost)
    {
        try
        {
            JsonObject obj = json.AsObject();
            JsonNode? contextJson = json[nameof(Context)];

            string extensionStr = obj["Extension"]!.AsValue().GetValue<string>();
            Type? extensionType = TypeFormat.ToType(extensionStr);
            OutputExtension? extension = Array.Find(extensionProvider.GetExtensions<OutputExtension>(),
                x => x.GetType() == extensionType);

            // Resolve a legacy filename in the scene directory that contains these profiles.
            // The directory can move, but sibling scene files must keep their own profiles.
            bool matchesObject = obj.TryGetPropertyValue("ObjectId", out JsonNode? objectId)
                ? objectId?.GetValue<Guid>() == editorContext.Object.Id.Value
                : obj["File"]?.GetValue<string>() is { Length: > 0 } file
                  && editorContext.Object.Uri is { IsFile: true } uri
                  && FilePathComparison.AreSameCanonicalPath(
                      Path.Combine(Path.GetDirectoryName(uri.LocalPath)!, Path.GetFileName(file.Replace('\\', '/'))),
                      uri.LocalPath);

            if (contextJson != null
                && extension != null
                && matchesObject
                && ExtensionGuard.Call(extension, () => extension.TryCreateContext(editorContext, out IOutputContext? created) ? created : null)
                    is { } context)
            {
                context.ReadFromJson(contextJson.AsObject());
                logger.LogInformation("OutputProfileItem created from JSON. ObjectId: {ObjectId}, Context: {Context}",
                    context.Object.Id.Value, context);
                return new OutputProfileItem(context, editorContext, runHost);
            }
            else
            {
                logger.LogWarning("Failed to create OutputProfileItem from JSON. ObjectId: {ObjectId}, Extension: {Extension}",
                    objectId, extensionStr);
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating OutputProfileItem from JSON.");
            return null;
        }
    }
}
