using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class OutputProfileItem : IDisposable, IOutputExecutionController
{
    private readonly ILogger<OutputProfileItem> _logger = Log.CreateLogger<OutputProfileItem>();
    private readonly EditorService _editorService;
    private readonly object _outputOperationSync = new();
    private readonly ReactivePropertySlim<bool> _isRunning = new();
    private IDisposable? _outputOperation;
    private CancellationTokenSource? _executionCancellation;
    private Task? _executionTask;
    private bool _disposeRequested;
    private bool _contextDisposed;

    public OutputProfileItem(IOutputContext context, IEditorContext editorContext, EditorService editorService)
    {
        Context = context;
        EditorContext = editorContext;
        _editorService = editorService;

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

            outputOperation = _editorService.TryBeginOutputOperation();
            if (outputOperation is not null)
            {
                try
                {
                    executionCancellation = new CancellationTokenSource();
                }
                catch
                {
                    outputOperation.Dispose();
                    throw;
                }

                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _outputOperation = outputOperation;
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

        IDisposable outputContextSuspension;
        try
        {
            // The execution task is published before changing reactive editor state, so a
            // re-entrant start observes and joins this execution instead of creating another one.
            outputContextSuspension = _editorService.SuspendEditor(EditorContext);
        }
        catch (Exception ex)
        {
            CompleteFailedStart(outputOperation, executionCancellation!, completion!, ex);
            execution = completion!.Task;
            return true;
        }

        _ = RunOutputAsync(
            outputOperation,
            outputContextSuspension,
            executionCancellation!,
            completion!);
        execution = completion!.Task;
        return true;
    }

    private void CompleteFailedStart(
        IDisposable outputOperation,
        CancellationTokenSource executionCancellation,
        TaskCompletionSource completion,
        Exception failure)
    {
        List<Exception>? failures = [failure];
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
            canceled: false,
            cancellationToken: default);
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
        IDisposable outputContextSuspension,
        CancellationTokenSource executionCancellation,
        TaskCompletionSource completion)
    {
        bool canceled = false;
        CancellationToken executionToken = executionCancellation.Token;
        List<Exception>? failures = null;
        try
        {
            _isRunning.Value = true;

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
            CaptureCleanupFailure(executionCancellation.Dispose, ref failures);

            // Restore the editor-facing state while the workspace is still reserved. Releasing the
            // output lease first would let a workspace mutation begin and race this restoration.
            CaptureCleanupFailure(outputContextSuspension.Dispose, ref failures);

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

        if (ReferenceEquals(_outputOperation, outputOperation))
        {
            _outputOperation = null;
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
            ["File"] = item.Context.Object.Uri!.LocalPath,
            [nameof(Context)] = ctxJson
        };
    }

    public static OutputProfileItem? FromJson(IEditorContext editorContext, JsonNode json, ILogger logger,
        ExtensionProvider extensionProvider, EditorService editorService)
    {
        try
        {
            JsonObject obj = json.AsObject();
            JsonNode? contextJson = json[nameof(Context)];

            string extensionStr = obj["Extension"]!.AsValue().GetValue<string>();
            Type? extensionType = TypeFormat.ToType(extensionStr);
            OutputExtension? extension = Array.Find(extensionProvider.GetExtensions<OutputExtension>(),
                x => x.GetType() == extensionType);

            string file = obj["File"]!.AsValue().GetValue<string>();

            if (contextJson != null
                && extension != null
                && File.Exists(file)
                && extension.TryCreateContext(
                    editorContext,
                    out IOutputContext? context))
            {
                context.ReadFromJson(contextJson.AsObject());
                logger.LogInformation("OutputProfileItem created from JSON. File: {File}, Context: {Context}", file,
                    context);
                return new OutputProfileItem(context, editorContext, editorService);
            }
            else
            {
                logger.LogWarning("Failed to create OutputProfileItem from JSON. File: {File}, Extension: {Extension}",
                    file, extensionStr);
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
