using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ViewModels;
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
        bool runningPublished = false;
        bool canceled = false;
        CancellationToken executionToken = executionCancellation.Token;
        List<Exception>? failures = null;
        try
        {
            runningPublished = true;
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

            if (runningPublished)
            {
                CaptureCleanupFailure(() => _isRunning.Value = false, ref failures);
            }

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
        while (true)
        {
            bool disposeContext;
            lock (_outputOperationSync)
            {
                disposeContext = _disposeRequested
                                 && !_contextDisposed
                                 && ReferenceEquals(_executionTask, completion.Task);
                if (disposeContext)
                {
                    _contextDisposed = true;
                }
                else
                {
                    // Keep admission until any deferred context disposal has finished, then release
                    // it immediately before publishing the terminal execution. Dispose linearizes
                    // on this lock, so a request that loses this race is an idle-profile disposal,
                    // not cleanup belonging to the completed output operation.
                    CaptureCleanupFailure(outputOperation.Dispose, ref failures);
                    if (ReferenceEquals(_outputOperation, outputOperation))
                    {
                        _outputOperation = null;
                    }

                    // Publish the terminal result before clearing the single-flight task. A
                    // concurrent start must either join this execution or observe it as already
                    // complete; it must never enter the context between those two state changes.
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

                    if (ReferenceEquals(_executionTask, completion.Task))
                    {
                        _executionTask = null;
                    }

                    return;
                }
            }

            if (disposeContext)
            {
                CaptureCleanupFailure(DisposeContext, ref failures);
            }
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
        _logger.LogInformation("Disposing OutputProfileItem for file: {File}", Context.Object.Uri);
        Exception? contextFailure = null;
        try
        {
            Context.Dispose();
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

public sealed class OutputService(EditViewModel editViewModel) : IDisposable
{
    private readonly CoreList<OutputProfileItem> _items = [];
    private readonly ReactivePropertySlim<OutputProfileItem?> _selectedItem = new();
    private readonly EditorService _editorService = editViewModel.EditorService;
    private readonly ExtensionProvider _extensionProvider = editViewModel.ExtensionProvider;

    private readonly string _filePath = Path.Combine(
        Path.GetDirectoryName(editViewModel.Scene.Uri!.LocalPath)!,
        EditorConstants.BeutlFolder, "output-profile.json");

    private readonly ILogger _logger = Log.CreateLogger<OutputService>();
    private bool _isRestored;

    public ICoreList<OutputProfileItem> Items => _items;

    public IReactiveProperty<OutputProfileItem?> SelectedItem => _selectedItem;

    public void AddItem(string file, OutputExtension extension)
    {
        if (!extension.TryCreateContext(
                editViewModel,
                out IOutputContext? context))
        {
            _logger.LogError("Failed to create context for file: {File}", file);
            throw new Exception("Failed to create context");
        }

        context.Name.Value = Items.Count == 0 ? "Default" : $"Profile {Items.Count}";
        var item = new OutputProfileItem(context, editViewModel, _editorService);
        Items.Add(item);
        SelectedItem.Value = item;
        _logger.LogInformation("Added new OutputProfileItem. File: {File}, Context: {Context}", file, context);
    }

    public OutputExtension[] GetExtensions(Type type)
    {
        return _extensionProvider
            .GetExtensions<OutputExtension>()
            .Where(x => x.IsSupported(type)).ToArray();
    }

    public void SaveItems()
    {
        if (!_isRestored) return;

        var array = new JsonArray();
        foreach (OutputProfileItem item in _items.GetMarshal().Value)
        {
            JsonNode json = OutputProfileItem.ToJson(item);
            array.Add(json);
        }

        array.JsonSave(_filePath);
        _logger.LogInformation("Saved {Count} OutputProfileItems to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        // 再試行時に前回の成功状態が残ると、IO/JSON 失敗後も SaveItems が有効になり
        // 空または不整合な _items が書き込まれてユーザーのプロファイルを消す恐れがある。
        _isRestored = false;
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("Output profile file not found: {FilePath}", _filePath);
                _isRestored = true;
                return;
            }

            using FileStream stream = File.Open(_filePath, FileMode.Open);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is not JsonArray jsonArray)
            {
                _logger.LogWarning("Invalid JSON format in output profile file: {FilePath}", _filePath);
                return;
            }

            var items = _items.ToArray();
            _items.Clear();
            _selectedItem.Value = null;
            foreach (OutputProfileItem item in items)
            {
                item.Dispose();
            }

            _items.EnsureCapacity(jsonArray.Count);

            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem == null) continue;

                var item = OutputProfileItem.FromJson(editViewModel, jsonItem, _logger, _extensionProvider, _editorService);
                if (item != null)
                {
                    _items.Add(item);
                }
            }

            // 既存ファイルの読み込みに成功した場合のみ書き込みを許可する。
            // try 冒頭で true にすると、IO 失敗や JSON 破損時に後続の SaveItems が
            // 空の _items を書き込み、ユーザーの保存済みプロファイルが消える。
            _isRestored = true;
            _logger.LogInformation("Restored {Count} OutputProfileItems from file: {FilePath}", _items.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring output profile file: {FilePath}", _filePath);
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing OutputService.");

        var items = _items.ToArray();
        _items.Clear();
        _selectedItem.Value = null;
        _selectedItem.Dispose();
        foreach (OutputProfileItem item in items)
        {
            item.Dispose();
        }

        _logger.LogInformation("OutputService disposed.");
    }
}
