using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal class DockHostViewModel : IAsyncDisposable
{
    private const int DockVersion = 2;
    private readonly string _sceneId;
    private readonly EditViewModel _editViewModel;
    private readonly ILogger _logger = Log.CreateLogger<DockHostViewModel>();
    private readonly object _disposeGate = new();
    private readonly SemaphoreSlim _layoutGate = new(1, 1);
    private Task? _disposeTask;
    private TaskCompletionSource? _layoutTransitionCompletion;
    private bool _disposing;
    private bool _layoutTransitioning;
    private readonly Dictionary<IToolContext, ToolDisposalRegistration> _toolDisposals =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<IToolContext, object> _disposedToolContexts = new();
    private readonly List<Task> _pendingDockableDisposals = [];
    private bool _layoutInitialized;
    private long _layoutEpoch;
    private bool _ownerShutdownRequested;
    internal IReadOnlyList<ToolTabExtension>? DefaultExtensionsOverride { get; set; }
    internal Func<Task>? BeforeDefaultTabMaterializationAsync { get; set; }
    internal Action<BeutlToolDockable>? BeforePrepareDockableDisposal { get; set; }
    internal Action<IRootDock>? BeforeLayoutPublication { get; set; }


    // Set only while ApplyLayout is walking a restore, so a failure mid-walk can dispose the tools
    // built so far. Null at every other time.
    private List<BeutlToolDockable>? _restoredTools;

    // Set while restoring an arrangement-only payload (a saved layout). Such a payload deliberately
    // omits tool state, so handing it to IToolContext.ReadFromJson would feed every reader a
    // document missing the fields its writer produces.
    private bool _restoringArrangementOnly;
    private static readonly AsyncLocal<DockHostViewModel?> s_materializingDefaults = new();

    internal bool IsMaterializingDefaultPluginCallback
        => ReferenceEquals(s_materializingDefaults.Value, this);

    internal void RequestOwnerShutdown()
    {
        lock (_disposeGate)
            _ownerShutdownRequested = true;
    }

    public DockHostViewModel(string sceneId, EditViewModel editViewModel)
    {
        _sceneId = sceneId;
        _editViewModel = editViewModel;
        Factory = new BeutlDockFactory(editViewModel);
        Factory.DisposalTracker = TrackDockableDisposal;
        Factory.LayoutMutated = () => Interlocked.Increment(ref _layoutEpoch);

        var placeholder = Factory.CreateRootDock();
        placeholder.Id = DockIds.Root;
        placeholder.IsCollapsable = false;
        Layout = new ReactivePropertySlim<IRootDock>(placeholder);
    }

    internal BeutlDockFactory Factory { get; }

    internal ReactivePropertySlim<IRootDock> Layout { get; }

    public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext
    {
        return Factory.EnumerateTools()
            .Select(t => t.ToolContext)
            .OfType<T>()
            .FirstOrDefault(condition);
    }

    public T? FindToolTab<T>() where T : IToolContext
    {
        return FindToolTab<T>(_ => true);
    }

    public IToolContext? FindToolContext(Type extensionType)
    {
        return Factory.EnumerateTools()
            .Select(t => t.ToolContext)
            .FirstOrDefault(ctx => ctx.Extension.GetType() == extensionType);
    }

    public async Task<bool> OpenToolTabAsync(IToolContext item, IToolDock? target = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        bool teardownReentrant = ToolContextDisposal.IsActive;
        await _layoutGate.WaitAsync();
        bool opened = false;
        bool rejected = false;
        bool hostedOrRegistered = false;
        bool alreadyDisposed = false;
        try
        {
            lock (_disposeGate)
            {
                alreadyDisposed = _disposedToolContexts.TryGetValue(item, out _);
                hostedOrRegistered = _toolDisposals.ContainsKey(item)
                    || Factory.EnumerateTools().Any(tool =>
                        tool.TryGetToolContext(out IToolContext? context)
                        && ReferenceEquals(context, item));
                rejected = teardownReentrant
                    || _ownerShutdownRequested
                    || _disposing
                    || _layoutTransitioning;
                if (!rejected)
                    opened = OpenToolTabCore(item, target);
                if (opened)
                    _layoutEpoch++;
            }
        }
        finally
        {
            _layoutGate.Release();
        }
        if (teardownReentrant)
        {
            if (!ToolContextDisposal.IsCurrent(item) && !alreadyDisposed)
            {
                Task deferred = hostedOrRegistered
                    ? CloseToolTabAsync(item)
                    : DisposeContextOnceAsync(item);
                _ = deferred.ContinueWith(
                    t => _logger.LogWarning(
                        t.Exception,
                        "Deferred reentrant tool disposal failed ({SceneId})",
                        _sceneId),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            return false;
        }
        if (rejected || !opened)
            await DisposeContextOnceAsync(item);
        await DrainPendingDockableDisposalsAsync();
        return opened && !rejected;
    }

    private bool OpenToolTabCore(IToolContext item, IToolDock? target)
    {
        _logger.LogInformation("Attempting to open tool tab '{ToolTabName}' ({SceneId})", item.Extension.Name, _sceneId);
        try
        {
            lock (_disposeGate)
            {
                if (_toolDisposals.ContainsKey(item)
                    || _disposedToolContexts.TryGetValue(item, out _))
                    return false;
            }
            EnsureDefaultLayout();

            var existing = Factory.EnumerateTools().FirstOrDefault(t => t.ToolContext == item);
            if (existing is not null)
            {
                Factory.SetActiveDockable(existing);
                return true;
            }

            if (!item.Extension.CanMultiple &&
                Factory.EnumerateTools().Any(t => t.ToolContext.Extension == item.Extension))
            {
                _logger.LogWarning("Tool tab '{ToolTabName}' cannot be opened multiple times. ({SceneId})", item.Extension.Name, _sceneId);
                return false;
            }

            // A target captured before a layout transition may belong to a detached root.
            // Resolve it against the current root before attaching anything.
            if (target is not null && !BeutlDockFactory.Traverse(Layout.Value).Contains(target))
                target = null;

            BeutlToolDockable? dockable = null;
            try
            {
                dockable = Factory.AddTool(item, target);
            }
            catch
            {
                // AddTool activates/focuses after attaching. If either callback throws, roll the
                // dockable back out of the live tree before the caller observes failure.
                if (dockable is not null)
                    Factory.DetachDockable(dockable);
                else
                {
                    dockable = Factory.EnumerateTools().FirstOrDefault(t => t.ToolContext == item);
                    if (dockable is not null)
                        Factory.DetachDockable(dockable);
                }
                if (dockable is not null)
                {
                    ToolDisposalRegistration rollback = PrepareDockableDisposal(dockable);
                    rollback.Start.TrySetResult();
                }
                throw;
            }
            if (dockable is null)
            {
                _logger.LogWarning("No dock zone found for tool '{ToolTabName}'. ({SceneId})", item.Extension.Name, _sceneId);
                return false;
            }
            _logger.LogInformation("Tool tab '{ToolTabName}' opened successfully. ({SceneId})", item.Extension.Name, _sceneId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open tool tab '{ToolTabName}'. ({SceneId})", item.Extension.Name, _sceneId);
            return false;
        }
    }

    public async Task CloseToolTabAsync(IToolContext item)
    {
        ArgumentNullException.ThrowIfNull(item);
        bool ownerCallbackReentrant = IsMaterializingDefaultPluginCallback;
        bool reentrantToolTeardown = ToolContextDisposal.IsActive || ownerCallbackReentrant;
        await _layoutGate.WaitAsync();
        BeutlToolDockable? dockable = null;
        bool rejected;
        Task? owningTransition = null;
        ToolDisposalRegistration? disposal = null;
        try
        {
            lock (_disposeGate)
            {
                if (_toolDisposals.TryGetValue(item, out disposal))
                {
                    rejected = true;
                    owningTransition = disposal.Completion.Task;
                    goto Release;
                }
                rejected = _ownerShutdownRequested
                    || _disposing
                    || (_layoutTransitioning && !ownerCallbackReentrant);
                if (rejected)
                {
                    owningTransition = _disposing
                        ? _disposeTask
                        : _layoutTransitionCompletion?.Task;
                }
                if (rejected)
                    goto Release;

                _logger.LogInformation("Attempting to close tool tab '{ToolName}' ({SceneId})", item.Extension.Name, _sceneId);
                try
                {
                    dockable = Factory.EnumerateTools().FirstOrDefault(t => t.ToolContext == item);
                    if (dockable is not null)
                    {
                        disposal = PrepareDockableDisposal(dockable);
                        Factory.DetachDockable(dockable);
                        _layoutEpoch++;
                    }
                    else
                    {
                        disposal = PrepareContextDisposal(
                            item,
                            () => ToolContextDisposal.DisposeAsync(item));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to close tool tab '{ToolName}'. ({SceneId})", item.Extension.Name, _sceneId);
                }
            }
        Release:
            ;
        }
        finally
        {
            _layoutGate.Release();
        }
        if (rejected)
        {
            if (!reentrantToolTeardown && owningTransition is not null)
                await owningTransition;
            return;
        }
        if (disposal is not null)
        {
            disposal.Start.TrySetResult();
            if (!reentrantToolTeardown)
                await disposal.Completion.Task;
        }
        if (!reentrantToolTeardown)
            await DrainPendingDockableDisposalsAsync();
    }

    public Task OpenDefaultTabsAsync() => OpenDefaultTabsCoreAsync(allowDuringTransition: false);

    private async Task OpenDefaultTabsCoreAsync(bool allowDuringTransition)
    {
        ToolTabExtension[] extensions;
        await _layoutGate.WaitAsync();
        try
        {
            lock (_disposeGate)
            {
                if (_ownerShutdownRequested
                    || _disposing
                    || (_layoutTransitioning && !allowDuringTransition))
                    return;
                EnsureDefaultLayout();
                IEnumerable<ToolTabExtension> availableExtensions = DefaultExtensionsOverride
                    ?? _editViewModel.ExtensionProvider.AllExtensions.OfType<ToolTabExtension>();
                extensions = availableExtensions
                    .Where(e => e.OpenByDefault)
                    .OrderBy(e => (int)e.DefaultAnchor)
                    .ThenBy(e => e.DefaultOrder)
                    .ToArray();
            }
        }
        finally
        {
            _layoutGate.Release();
        }

        _logger.LogInformation("Opening default tabs ({SceneId})", _sceneId);
        if (allowDuringTransition && BeforeDefaultTabMaterializationAsync is { } beforeMaterialization)
            await beforeMaterialization();
        foreach (var ext in extensions)
        {
            IToolDock? target;
            await _layoutGate.WaitAsync();
            try
            {
                target = Factory.GetAnchoredDock(ext.DefaultAnchor)
                    ?? Factory.GetAnchoredDock(DockAnchor.Left)
                    ?? Factory.FindFirstToolDock();
            }
            finally
            {
                _layoutGate.Release();
            }
            await OpenToolTabFromExtensionCoreAsync(
                ext,
                target,
                allowDuringTransition,
                ownerCallbackScope: true);
        }

        await _layoutGate.WaitAsync();
        try
        {
            if (Factory.GetAnchoredDock(DockAnchor.Bottom) is { } bottomDock)
                bottomDock.ActiveDockable = bottomDock.VisibleDockables?.FirstOrDefault();
            if (Factory.GetAnchoredDock(DockAnchor.Left) is { } leftDock)
                leftDock.ActiveDockable = leftDock.VisibleDockables?.FirstOrDefault();
            if (Factory.GetAnchoredDock(DockAnchor.Right) is { } rightDock)
                rightDock.ActiveDockable = rightDock.VisibleDockables?.FirstOrDefault();
        }
        finally
        {
            _layoutGate.Release();
        }
    }

    internal Task<bool> OpenToolTabFromExtensionAsync(ToolTabExtension ext, IToolDock? target)
        => OpenToolTabFromExtensionCoreAsync(
            ext,
            target,
            allowDuringTransition: false,
            ownerCallbackScope: false);

    private async Task<bool> OpenToolTabFromExtensionCoreAsync(
        ToolTabExtension ext,
        IToolDock? target,
        bool allowDuringTransition,
        bool ownerCallbackScope)
    {
        bool rejected;
        await _layoutGate.WaitAsync();
        try
        {
            lock (_disposeGate)
                rejected = _ownerShutdownRequested
                    || _disposing
                    || (_layoutTransitioning && !allowDuringTransition);
        }
        finally
        {
            _layoutGate.Release();
        }

        IToolContext? tab = null;
        bool created = false;
        DockHostViewModel? previousMaterializer = s_materializingDefaults.Value;
        if (ownerCallbackScope)
            s_materializingDefaults.Value = this;
        try
        {
            created = !rejected && ext.TryCreateContext(_editViewModel, out tab) && tab is not null;
        }
        finally
        {
            if (ownerCallbackScope)
            {
                s_materializingDefaults.Value = previousMaterializer;
                await DrainPendingDockableDisposalsAsync();
            }
        }

        if (!created || tab is null)
        {
            // [NotNullWhen(true)] permits an extension to return false while
            // still handing us a context. Consume that context on the refusal
            // path as well; otherwise a contract-violating extension leaks its
            // subscriptions until the editor itself is torn down.
            if (tab is not null)
                await DisposeContextOnceAsync(tab);
            return false;
        }

        if (allowDuringTransition)
        {
            bool opened;
            await _layoutGate.WaitAsync();
            try
            {
                lock (_disposeGate)
                {
                    opened = !_ownerShutdownRequested && !_disposing && OpenToolTabCore(tab, target);
                    if (opened)
                        _layoutEpoch++;
                }
            }
            finally
            {
                _layoutGate.Release();
            }
            if (!opened)
                await DisposeContextOnceAsync(tab);
            await DrainPendingDockableDisposalsAsync();
            return opened;
        }

        return await OpenToolTabAsync(tab, target);
    }

    private void EnsureDefaultLayout()
    {
        if (_layoutInitialized) return;
        var layout = Factory.CreateLayout();
        Factory.InitLayout(layout);
        Layout.Value = layout;
        _layoutInitialized = true;
        _layoutEpoch++;
    }

    public ValueTask DisposeAsync() => new(GetDisposeTask());

    internal async Task WaitForLayoutTransitionAsync()
    {
        for (; ; )
        {
            Task? transition;
            lock (_disposeGate)
                transition = _layoutTransitionCompletion?.Task;
            if (transition is null)
                return;
            await transition.ConfigureAwait(false);
        }
    }

    internal Task GetDisposeTask()
    {
        TaskCompletionSource? completion = null;
        Task task;
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            _disposing = true;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            task = completion.Task;
        }

        _ = CompleteDisposeAsync(completion);
        return task;
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dock host disposal failed ({SceneId})", _sceneId);
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task? transition;
        lock (_disposeGate)
            transition = _layoutTransitionCompletion?.Task;
        if (transition is not null)
            await transition;

        BeutlToolDockable[] dockables;
        ToolDisposalRegistration[] disposals;
        await _layoutGate.WaitAsync();
        try
        {
            _logger.LogInformation("Disposing DockHostViewModel ({SceneId})", _sceneId);
            dockables = Factory.EnumerateTools().ToArray();
            var prepared = new List<ToolDisposalRegistration>(dockables.Length);
            foreach (BeutlToolDockable dockable in dockables)
            {
                try { prepared.Add(PrepareDockableDisposal(dockable)); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to prepare a tool for editor disposal ({SceneId})", _sceneId);
                }
            }
            disposals = prepared.ToArray();
            foreach (BeutlToolDockable dockable in dockables)
            {
                Factory.DetachDockable(dockable);
            }
        }
        finally
        {
            _layoutGate.Release();
        }

        foreach (ToolDisposalRegistration disposal in disposals)
            disposal.Start.TrySetResult();
        await DisposeDockablesAsync(dockables, "editor disposal");
        await AwaitAllToolDisposalsAsync();
    }

    public void WriteToJson(JsonObject json)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => WriteToJson(json));
            return;
        }
        _logger.LogInformation("Writing DockHostViewModel to JSON ({SceneId})", _sceneId);
        IRootDock layout;
        long epoch;
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_layoutTransitioning)
                throw new InvalidOperationException("Dock layout transition is in progress.");
            layout = Layout.Value;
            epoch = _layoutEpoch;
        }
        var snapshot = new JsonObject
        {
            ["_dockVersion"] = DockVersion,
            ["DockLayout"] = SaveNode(layout),
        };
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_layoutTransitioning || epoch != _layoutEpoch)
                throw new InvalidOperationException("Dock layout changed while it was being serialized.");
        }
        json.Clear();
        foreach ((string key, JsonNode? value) in snapshot)
            json[key] = value?.DeepClone();
    }

    public async Task<bool> ReadFromJsonAsync(JsonObject json)
    {
        bool applied = await ApplyLayoutCoreAsync(json, restoreToolState: true);
        if (applied)
            return true;

        bool needsDefaults;
        await _layoutGate.WaitAsync();
        try
        {
            needsDefaults = !Factory.EnumerateTools().Any();
        }
        finally
        {
            _layoutGate.Release();
        }
        if (needsDefaults)
            await OpenDefaultTabsAsync();
        return false;
    }

    public Task ResetLayoutAsync()
    {
        return ResetLayoutCoreAsync();
    }

    private async Task ResetLayoutCoreAsync()
    {
        BeutlToolDockable[] previousTools;
        ToolDisposalRegistration[] disposals;
        Exception? preparationFailure = null;
        TaskCompletionSource transitionCompletion;
        await _layoutGate.WaitAsync();
        try
        {
            lock (_disposeGate)
            {
                if (_disposing || _layoutTransitioning)
                    return;
                _layoutTransitioning = true;
                _layoutEpoch++;
                transitionCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _layoutTransitionCompletion = transitionCompletion;
            }

            try
            {
                previousTools = Factory.EnumerateTools().ToArray();
                var prepared = new List<ToolDisposalRegistration>(previousTools.Length);
                foreach (BeutlToolDockable tool in previousTools)
                {
                    try { prepared.Add(PrepareDockableDisposal(tool)); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to prepare a tool for layout reset ({SceneId})", _sceneId);
                        preparationFailure ??= ex;
                    }
                }
                disposals = prepared.ToArray();
                foreach (BeutlToolDockable tool in previousTools)
                    Factory.DetachDockable(tool);
            }
            catch
            {
                EndLayoutTransition(transitionCompletion);
                throw;
            }
        }
        finally
        {
            _layoutGate.Release();
        }

        try
        {
            foreach (ToolDisposalRegistration disposal in disposals)
                disposal.Start.TrySetResult();
            await DisposeDockablesAsync(previousTools, "reset");
            await AwaitAllToolDisposalsAsync();
            if (preparationFailure is not null)
                ExceptionDispatchInfo.Capture(preparationFailure).Throw();
            await CompleteTransitionWithDefaultLayoutAsync("user requested");
        }
        finally
        {
            EndLayoutTransition(transitionCompletion);
        }
    }

    /// <summary>
    /// Captures the current layout in the same shape <see cref="WriteToJson"/> writes, for
    /// <see cref="ApplyLayoutAsync"/> to restore later.
    /// </summary>
    /// <remarks>
    /// Per-tool state (selected element ids, search text, ...) is dropped; it belongs to the scene
    /// it was captured from.
    /// </remarks>
    public JsonObject CaptureLayout()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(CaptureLayout);
        if (!_layoutInitialized)
            EnsureDefaultLayout();
        IRootDock layout;
        long epoch;
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_layoutTransitioning)
                throw new InvalidOperationException("Dock layout transition is in progress.");
            layout = Layout.Value;
            epoch = _layoutEpoch;
        }
        var snapshot = new JsonObject
        {
            ["_dockVersion"] = DockVersion,
            ["DockLayout"] = SaveNode(layout, includeToolState: false),
        };
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (_layoutTransitioning || epoch != _layoutEpoch)
                throw new InvalidOperationException("Dock layout changed while it was being captured.");
        }
        return snapshot;
    }

    /// <summary>
    /// Replaces the current layout with a previously captured one.
    /// </summary>
    /// <remarks>
    /// The outgoing tool contexts are disposed and the incoming layout builds fresh ones against
    /// this editor, so a layout captured elsewhere restores the arrangement only.
    /// </remarks>
    public Task<bool> ApplyLayoutAsync(JsonObject layout)
    {
        return ApplyLayoutCoreAsync(layout, restoreToolState: false);
    }

    private async Task<bool> ApplyLayoutCoreAsync(
        JsonObject layout,
        bool restoreToolState)
    {
        if (!TryCreateLayoutPlan(layout, out LayoutPlan? plan))
            return false;

        IRootDock? restored = null;
        List<BeutlToolDockable>? previousTools = null;
        ToolDisposalRegistration[]? previousDisposals = null;
        Exception? preparationFailure = null;
        bool transitionActive = false;
        bool restoredPublished = false;
        TaskCompletionSource? transitionCompletion = null;
        await _layoutGate.WaitAsync();
        try
        {
            lock (_disposeGate)
            {
                if (_disposing || _layoutTransitioning)
                    return false;
                _layoutTransitioning = true;
                _layoutEpoch++;
                transitionActive = true;
                transitionCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _layoutTransitionCompletion = transitionCompletion;
            }

            try
            {
                previousTools = Factory.EnumerateTools().ToList();
                var prepared = new List<ToolDisposalRegistration>(previousTools.Count);
                foreach (BeutlToolDockable tool in previousTools)
                {
                    try { prepared.Add(PrepareDockableDisposal(tool)); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to prepare a tool for layout replacement ({SceneId})", _sceneId);
                        preparationFailure ??= ex;
                    }
                }
                previousDisposals = prepared.ToArray();
                foreach (BeutlToolDockable tool in previousTools)
                    Factory.DetachDockable(tool);
            }
            catch
            {
                EndLayoutTransition(transitionCompletion);
                transitionActive = false;
                throw;
            }
        }
        finally
        {
            _layoutGate.Release();
        }

        try
        {
            foreach (ToolDisposalRegistration disposal in previousDisposals!)
                disposal.Start.TrySetResult();
            await DisposeDockablesAsync(previousTools!, "layout replacement");
            await AwaitAllToolDisposalsAsync();
            if (preparationFailure is not null)
                ExceptionDispatchInfo.Capture(preparationFailure).Throw();

            restored = await TryRestoreLayoutAsync(plan!, restoreToolState);
            if (restored is null)
            {
                await CompleteTransitionWithDefaultLayoutAsync("saved layout materialization failed");
                EndLayoutTransition(transitionCompletion!);
                transitionActive = false;
                return false;
            }

            bool openDefaults;
            await _layoutGate.WaitAsync();
            try
            {
                BeforeLayoutPublication?.Invoke(restored!);
                Factory.SetRootDock(restored!);
                Factory.InitLayout(restored!);
                Layout.Value = restored!;
                _layoutInitialized = true;
                restoredPublished = true;
                _layoutEpoch++;
                openDefaults = !Factory.EnumerateTools().Any();
            }
            finally
            {
                _layoutGate.Release();
            }
            if (openDefaults)
                await OpenDefaultTabsCoreAsync(allowDuringTransition: true);
            EndLayoutTransition(transitionCompletion!);
            transitionActive = false;
            return true;
        }
        catch (Exception publicationError)
        {
            Exception? recoveryError = null;
            try
            {
                if (restored is not null)
                {
                    BeutlToolDockable[] restoredTools = BeutlDockFactory.Traverse(restored)
                        .OfType<BeutlToolDockable>()
                        .Distinct()
                        .ToArray();
                    ToolDisposalRegistration[] restoredDisposals = restoredTools
                        .Select(PrepareDockableDisposal)
                        .ToArray();
                    foreach (BeutlToolDockable tool in restoredTools)
                        Factory.DetachDockable(tool);
                    foreach (ToolDisposalRegistration disposal in restoredDisposals)
                        disposal.Start.TrySetResult();
                    await DisposeDockablesAsync(restoredTools, "failed layout publication");
                    await AwaitAllToolDisposalsAsync();
                }

                await _layoutGate.WaitAsync();
                try
                {
                    _layoutInitialized = false;
                    ResetToDefaultLayout(restoredPublished
                        ? "saved layout publication failed"
                        : "saved layout restoration failed");
                }
                finally
                {
                    _layoutGate.Release();
                }
            }
            catch (Exception ex)
            {
                recoveryError = ex;
            }
            if (recoveryError is not null)
                throw new AggregateException(publicationError, recoveryError);
            ExceptionDispatchInfo.Capture(publicationError).Throw();
            throw;
        }
        finally
        {
            if (transitionActive)
                EndLayoutTransition(transitionCompletion!);
        }
    }

    private void EndLayoutTransition(TaskCompletionSource completion)
    {
        lock (_disposeGate)
        {
            if (ReferenceEquals(_layoutTransitionCompletion, completion))
            {
                _layoutTransitioning = false;
                _layoutTransitionCompletion = null;
            }
        }
        completion.TrySetResult();
    }

    private async Task CompleteTransitionWithDefaultLayoutAsync(string reason)
    {
        await _layoutGate.WaitAsync();
        try
        {
            ResetToDefaultLayout(reason);
        }
        finally
        {
            _layoutGate.Release();
        }
        // Keep the transition flag set while default tabs materialize. Internal opens are
        // explicitly admitted, while concurrent external opens continue to be rejected.
        await OpenDefaultTabsCoreAsync(allowDuringTransition: true);
    }

    private async Task DisposeDockablesAsync(
        IEnumerable<BeutlToolDockable> dockables,
        string operation)
    {
        foreach (BeutlToolDockable tool in dockables)
        {
            IToolContext? context = null;
            tool.TryGetToolContext(out context);
            try
            {
                await tool.GetDisposeTask();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to dispose a tool during {Operation} ({SceneId})",
                    operation,
                    _sceneId);
            }
            if (context is not null)
            {
                lock (_disposeGate)
                    _disposedToolContexts.GetValue(context, static _ => new object());
            }
        }
    }

    private bool TryCreateLayoutPlan(JsonObject layout, out LayoutPlan? plan)
    {
        plan = null;
        JsonObject snapshot = (JsonObject)layout.DeepClone();
        if (!IsCurrentVersion(snapshot)
            || !snapshot.TryGetPropertyValue("DockLayout", out JsonNode? node)
            || node is not JsonObject root
            || !root.TryGetPropertyValueAsJsonValue("$type", out string? rootType)
            || rootType != "root")
            return false;
        if (!ValidateNode(root))
            return false;
        plan = new LayoutPlan(snapshot);
        return true;

        bool ValidateNode(JsonObject obj)
        {
            if (!obj.TryGetPropertyValueAsJsonValue("$type", out string? type))
                return false;
            return type switch
            {
                "splitter" or "player" => true,
                "tool" => obj["extension"] is JsonObject ext
                    && ext.TryGetDiscriminator(out Type? extensionType)
                    && extensionType is not null
                    && _editViewModel.ExtensionProvider.AllExtensions
                        .OfType<ToolTabExtension>()
                        .Any(candidate => candidate.GetType() == extensionType)
                    && ValidateOptionalString(obj, "id"),
                "root" => ValidateOptionalString(obj, "id")
                    && ValidateChildren(obj, "children")
                    && ValidateChildren(obj, "hidden")
                    && ValidateChildren(obj, "leftPinned")
                    && ValidateChildren(obj, "rightPinned")
                    && ValidateChildren(obj, "topPinned")
                    && ValidateChildren(obj, "bottomPinned")
                    && ValidateWindows(obj),
                "proportional" => ValidateOptionalString(obj, "id")
                    && ValidateOptionalEnumString(obj, "orientation", "horizontal", "vertical")
                    && ValidateOptionalDouble(obj, "proportion", nonNegative: true)
                    && ValidateChildren(obj, "children"),
                "tool_dock" => ValidateOptionalString(obj, "id")
                    && ValidateOptionalString(obj, "alignment")
                    && ValidateOptionalDouble(obj, "proportion", nonNegative: true)
                    && ValidateOptionalDouble(obj, "minWidth", nonNegative: true)
                    && ValidateOptionalDouble(obj, "minHeight", nonNegative: true)
                    && ValidateOptionalInt32(obj, "activeDockableIndex")
                    && ValidateChildren(obj, "tools"),
                _ => false,
            };
        }

        bool ValidateChildren(JsonObject obj, string key)
        {
            if (!obj.TryGetPropertyValue(key, out JsonNode? node))
                return true;
            return node is null
                || node is JsonArray array
                    && array.All(x => x is JsonObject child && ValidateNode(child));
        }

        bool ValidateWindows(JsonObject obj)
        {
            if (!obj.TryGetPropertyValue("windows", out JsonNode? node) || node is null)
                return true;
            return node is JsonArray windows && windows.All(item =>
                item is JsonObject window
                && window["layout"] is JsonObject windowLayout
                && ValidateNode(windowLayout)
                && ValidateOptionalDouble(window, "x", nonNegative: false)
                && ValidateOptionalDouble(window, "y", nonNegative: false)
                && ValidateOptionalDouble(window, "width", nonNegative: true)
                && ValidateOptionalDouble(window, "height", nonNegative: true)
                && ValidateOptionalBoolean(window, "topmost")
                && ValidateOptionalString(window, "title"));
        }

        static bool ValidateOptionalString(JsonObject obj, string key)
            => !obj.TryGetPropertyValue(key, out JsonNode? node)
                || node is null
                || node is JsonValue value && value.TryGetValue(out string? _);

        static bool ValidateOptionalEnumString(
            JsonObject obj,
            string key,
            params string[] values)
        {
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node is null)
                return true;
            return node is JsonValue value
                && value.TryGetValue(out string? text)
                && text is not null
                && values.Contains(text, StringComparer.Ordinal);
        }

        static bool ValidateOptionalDouble(
            JsonObject obj,
            string key,
            bool nonNegative)
        {
            if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node is null)
                return true;
            return node is JsonValue value
                && value.TryGetValue(out double number)
                && double.IsFinite(number)
                && (!nonNegative || number >= 0);
        }

        static bool ValidateOptionalInt32(JsonObject obj, string key)
            => !obj.TryGetPropertyValue(key, out JsonNode? node)
                || node is null
                || node is JsonValue value && value.TryGetValue(out int _);

        static bool ValidateOptionalBoolean(JsonObject obj, string key)
            => !obj.TryGetPropertyValue(key, out JsonNode? node)
                || node is null
                || node is JsonValue value && value.TryGetValue(out bool _);
    }

    private async Task<IRootDock?> TryRestoreLayoutAsync(
        LayoutPlan plan,
        bool restoreToolState)
    {
        var built = new List<BeutlToolDockable>();
        IRootDock? result = null;
        _restoredTools = built;
        _restoringArrangementOnly = !restoreToolState;
        try
        {
            JsonObject layout = plan.Snapshot;
            if (!layout.TryGetPropertyValue("DockLayout", out JsonNode? node)
                || node is not JsonObject obj)
                return null;
            result = RestoreNode(obj) as IRootDock;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to materialize saved dock layout ({SceneId})", _sceneId);
            return null;
        }
        finally
        {
            _restoredTools = null;
            _restoringArrangementOnly = false;
            // A malformed extension may return false with a non-null context.
            // Drain that compensating disposal before the restored layout is
            // published so no rejected context survives the transition.
            await DrainPendingDockableDisposalsAsync();
            if (result is null)
            {
                try
                {
                    await Task.WhenAll(built.Select(static t => t.DisposeAsync().AsTask()));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose partially restored tools ({SceneId})", _sceneId);
                }
                await AwaitAllToolDisposalsAsync();
            }
        }
    }

    private sealed record LayoutPlan(JsonObject Snapshot);

    private async Task DrainPendingDockableDisposalsAsync()
    {
        for (; ; )
        {
            Task[] pending;
            lock (_disposeGate)
            {
                pending = _pendingDockableDisposals.ToArray();
                _pendingDockableDisposals.Clear();
            }
            if (pending.Length == 0)
                return;
            try
            {
                await Task.WhenAll(pending);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "One or more pending dockable disposals failed ({SceneId})", _sceneId);
            }
        }
    }

    private async Task AwaitAllToolDisposalsAsync()
    {
        Task[] disposals;
        lock (_disposeGate)
            disposals = _toolDisposals.Values
                .Select(static registration => registration.Completion.Task)
                .Distinct()
                .ToArray();
        if (disposals.Length == 0)
            return;
        try
        {
            await Task.WhenAll(disposals);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "One or more tool disposals failed ({SceneId})", _sceneId);
        }
    }

    private Task TrackDockableDisposal(BeutlToolDockable dockable)
    {
        lock (_disposeGate)
            _layoutEpoch++;
        Task disposal = DisposeDockableOnceAsync(dockable);
        _ = disposal.ContinueWith(
            t => _logger.LogWarning(
                t.Exception,
                "User-closed tool disposal failed ({SceneId})",
                _sceneId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return disposal;
    }

    private Task DisposeDockableOnceAsync(BeutlToolDockable dockable)
    {
        ToolDisposalRegistration registration = PrepareDockableDisposal(dockable);
        registration.Start.TrySetResult();
        return registration.Completion.Task;
    }

    private Task DisposeContextOnceAsync(IToolContext context)
    {
        ToolDisposalRegistration registration = PrepareContextDisposal(
            context,
            () => ToolContextDisposal.DisposeAsync(context));
        registration.Start.TrySetResult();
        return registration.Completion.Task;
    }

    private ToolDisposalRegistration PrepareDockableDisposal(BeutlToolDockable dockable)
    {
        BeforePrepareDockableDisposal?.Invoke(dockable);
        return dockable.TryGetToolContext(out IToolContext? context)
            ? PrepareContextDisposal(context, dockable.DisposeAsync)
            : ToolDisposalRegistration.Completed;
    }

    private ToolDisposalRegistration PrepareContextDisposal(
        IToolContext context,
        Func<ValueTask> dispose)
    {
        lock (_disposeGate)
        {
            if (_toolDisposals.TryGetValue(context, out ToolDisposalRegistration? existing))
                return existing;
            if (_disposedToolContexts.TryGetValue(context, out _))
                return ToolDisposalRegistration.Completed;
            var registration = new ToolDisposalRegistration(dispose);
            _toolDisposals.Add(context, registration);
            _pendingDockableDisposals.Add(registration.Completion.Task);
            _ = CompleteToolDisposalAsync(context, registration);
            return registration;
        }
    }

    private async Task CompleteToolDisposalAsync(
        IToolContext context,
        ToolDisposalRegistration registration)
    {
        await registration.Start.Task.ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            await registration.Dispose();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        // Remove every strong reference before waking callers. Otherwise a caller can observe a
        // completed close while the registration still retains the context and its dockable.
        OnDisposalCompleted(context, registration);
        if (failure is null)
            registration.Completion.TrySetResult();
        else
            registration.Completion.TrySetException(failure);
    }

    private void OnDisposalCompleted(IToolContext context, ToolDisposalRegistration registration)
    {
        lock (_disposeGate)
        {
            if (ReferenceEquals(_toolDisposals.GetValueOrDefault(context), registration))
                _toolDisposals.Remove(context);
            _disposedToolContexts.GetValue(context, static _ => new object());
            _pendingDockableDisposals.Remove(registration.Completion.Task);
        }
    }

    private sealed class ToolDisposalRegistration(Func<ValueTask> dispose)
    {
        public static readonly ToolDisposalRegistration Completed = CreateCompleted();

        public Func<ValueTask> Dispose { get; } = dispose;

        public TaskCompletionSource Start { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private static ToolDisposalRegistration CreateCompleted()
        {
            var registration = new ToolDisposalRegistration(static () => ValueTask.CompletedTask);
            registration.Start.TrySetResult();
            registration.Completion.TrySetResult();
            return registration;
        }
    }

    private static bool IsCurrentVersion(JsonObject json)
    {
        return json.TryGetPropertyValue("_dockVersion", out JsonNode? node)
               && node is JsonValue value
               && value.TryGetValue(out int version)
               && version == DockVersion;
    }

    private void ResetToDefaultLayout(string reason)
    {
        _logger.LogWarning("Resetting dock layout to defaults ({Reason}, {SceneId})", reason, _sceneId);
        _layoutInitialized = false;
        EnsureDefaultLayout();
    }

    private JsonObject SaveNode(IDockable node, bool includeToolState = true)
    {
        return node switch
        {
            IRootDock root => SaveRootDock(root, includeToolState),
            IProportionalDockSplitter => new JsonObject { ["$type"] = "splitter" },
            IProportionalDock prop => SaveProportionalDock(prop, includeToolState),
            IToolDock toolDock => SaveToolDock(toolDock, includeToolState),
            BeutlToolDockable tool => SaveBeutlTool(tool, includeToolState),
            PlayerToolDockable => new JsonObject { ["$type"] = "player" },
            _ => new JsonObject { ["$type"] = "unknown" },
        };
    }

    private JsonObject SaveRootDock(IRootDock root, bool includeToolState)
    {
        var obj = new JsonObject
        {
            ["$type"] = "root",
            ["id"] = root.Id
        };

        if (root.VisibleDockables is { Count: > 0 } visible)
        {
            var children = new JsonArray();
            foreach (var child in visible)
                children.Add(SaveNode(child, includeToolState));
            obj["children"] = children;
        }

        SaveDockableList(obj, "hidden", root.HiddenDockables, includeToolState);
        SaveDockableList(obj, "leftPinned", root.LeftPinnedDockables, includeToolState);
        SaveDockableList(obj, "rightPinned", root.RightPinnedDockables, includeToolState);
        SaveDockableList(obj, "topPinned", root.TopPinnedDockables, includeToolState);
        SaveDockableList(obj, "bottomPinned", root.BottomPinnedDockables, includeToolState);

        if (root.Windows is { Count: > 0 } windows)
        {
            var windowsArray = new JsonArray();
            foreach (var w in windows)
            {
                if (w.Layout is null) continue;
                var wObj = new JsonObject
                {
                    ["layout"] = SaveNode(w.Layout, includeToolState),
                    ["x"] = w.X,
                    ["y"] = w.Y,
                    ["width"] = w.Width,
                    ["height"] = w.Height,
                    ["topmost"] = w.Topmost,
                };
                if (!string.IsNullOrEmpty(w.Title))
                    wObj["title"] = w.Title;
                windowsArray.Add(wObj);
            }

            obj["windows"] = windowsArray;
        }

        return obj;
    }

    private void SaveDockableList(JsonObject parent, string key, IList<IDockable>? list, bool includeToolState)
    {
        if (list is not { Count: > 0 }) return;
        var array = new JsonArray();
        foreach (var item in list)
            array.Add(SaveNode(item, includeToolState));
        parent[key] = array;
    }

    private JsonObject SaveProportionalDock(IProportionalDock prop, bool includeToolState)
    {
        var obj = new JsonObject
        {
            ["$type"] = "proportional",
            ["id"] = prop.Id,
            ["orientation"] = prop.Orientation == Orientation.Horizontal ? "horizontal" : "vertical",
        };
        if (!double.IsNaN(prop.Proportion))
            obj["proportion"] = prop.Proportion;

        if (prop.VisibleDockables is { Count: > 0 } visible)
        {
            var children = new JsonArray();
            foreach (var child in visible)
                children.Add(SaveNode(child, includeToolState));
            obj["children"] = children;
        }

        return obj;
    }

    private JsonObject SaveToolDock(IToolDock toolDock, bool includeToolState)
    {
        var obj = new JsonObject
        {
            ["$type"] = "tool_dock",
            ["id"] = toolDock.Id,
            ["alignment"] = toolDock.Alignment.ToString().ToLowerInvariant(),
            ["minWidth"] = toolDock.MinWidth,
            ["minHeight"] = toolDock.MinHeight,
        };
        if (!double.IsNaN(toolDock.Proportion))
            obj["proportion"] = toolDock.Proportion;

        if (toolDock.VisibleDockables is { Count: > 0 } visible)
        {
            var tools = new JsonArray();
            int activeDockableIndex = -1;
            for (int i = 0; i < visible.Count; i++)
            {
                var child = visible[i];
                tools.Add(SaveNode(child, includeToolState));
                if (child == toolDock.ActiveDockable)
                    activeDockableIndex = i;
            }

            obj["tools"] = tools;
            if (activeDockableIndex >= 0)
                obj["activeDockableIndex"] = activeDockableIndex;
        }

        return obj;
    }

    private static JsonObject SaveBeutlTool(BeutlToolDockable dockable, bool includeToolState)
    {
        var ctx = dockable.ToolContext;
        var obj = new JsonObject
        {
            ["$type"] = "tool",
            ["id"] = dockable.Id
        };
        var extObj = new JsonObject();
        extObj.WriteDiscriminator(ctx.Extension.GetType());
        obj["extension"] = extObj;

        // A tool's serializer can have side effects (some write their own per-scene state file), so
        // a preset capture must not invoke it just to discard the result.
        if (includeToolState)
        {
            ctx.WriteToJson(obj);
        }

        return obj;
    }

    private IDockable? RestoreNode(JsonObject obj)
    {
        if (!obj.TryGetPropertyValueAsJsonValue("$type", out string? type))
            return null;

        return type switch
        {
            "root" => RestoreRootDock(obj),
            "proportional" => RestoreProportionalDock(obj),
            "splitter" => Factory.CreateProportionalDockSplitter(),
            "tool_dock" => RestoreToolDock(obj),
            "tool" => RestoreBeutlTool(obj),
            "player" => RestorePlayerDockable(),
            _ => null,
        };
    }

    private IRootDock RestoreRootDock(JsonObject obj)
    {
        var rootDock = Factory.CreateRootDock();
        rootDock.Id = obj["id"]?.GetValue<string>() ?? DockIds.Root;
        rootDock.Title = "Editor";
        rootDock.IsCollapsable = false;

        var children = RestoreChildren(obj);
        rootDock.VisibleDockables = Factory.CreateList<IDockable>(children.ToArray());
        if (rootDock.VisibleDockables.Count > 0)
        {
            rootDock.ActiveDockable = rootDock.VisibleDockables[0];
            rootDock.DefaultDockable = rootDock.VisibleDockables[0];
        }

        rootDock.HiddenDockables = RestoreDockableList(obj, "hidden");
        rootDock.LeftPinnedDockables = RestoreDockableList(obj, "leftPinned");
        rootDock.RightPinnedDockables = RestoreDockableList(obj, "rightPinned");
        rootDock.TopPinnedDockables = RestoreDockableList(obj, "topPinned");
        rootDock.BottomPinnedDockables = RestoreDockableList(obj, "bottomPinned");

        // Restore floating windows
        if (obj.TryGetPropertyValue("windows", out var wNode) && wNode is JsonArray wArray)
        {
            foreach (var wItem in wArray)
            {
                if (wItem is not JsonObject wObj) continue;
                if (!wObj.TryGetPropertyValue("layout", out var layoutNode) || layoutNode is not JsonObject layoutObj) continue;
                var layout = RestoreNode(layoutObj);
                if (layout is null) continue;

                if (!BeutlDockFactory.Traverse(layout).Any(i => i is BeutlToolDockable or PlayerToolDockable))
                {
                    continue;
                }

                var window = Factory.CreateDockWindow();
                window.Layout = layout as IRootDock ?? CreateWindowRootDock(layout);
                if (wObj["x"] is JsonValue xVal && xVal.TryGetValue(out double x)) window.X = x;
                if (wObj["y"] is JsonValue yVal && yVal.TryGetValue(out double y)) window.Y = y;
                if (wObj["width"] is JsonValue wVal && wVal.TryGetValue(out double width)) window.Width = width;
                if (wObj["height"] is JsonValue hVal && hVal.TryGetValue(out double height)) window.Height = height;
                if (wObj["topmost"] is JsonValue tVal && tVal.TryGetValue(out bool topmost)) window.Topmost = topmost;
                if (wObj["title"] is JsonValue titleVal && titleVal.TryGetValue(out string? title)) window.Title = title ?? string.Empty;
                rootDock.Windows ??= Factory.CreateList<IDockWindow>();
                rootDock.Windows.Add(window);
            }
        }

        return rootDock;
    }

    private IList<IDockable>? RestoreDockableList(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            return null;
        var list = new List<IDockable>();
        foreach (var item in array)
        {
            if (item is not JsonObject itemObj) continue;
            var restored = RestoreNode(itemObj);
            if (restored is not null) list.Add(restored);
        }

        return list.Count == 0 ? null : Factory.CreateList<IDockable>(list.ToArray());
    }

    private IRootDock CreateWindowRootDock(IDockable content)
    {
        var windowRoot = Factory.CreateRootDock();
        windowRoot.VisibleDockables = Factory.CreateList<IDockable>(content);
        windowRoot.ActiveDockable = content;
        return windowRoot;
    }

    private IProportionalDock RestoreProportionalDock(JsonObject obj)
    {
        var dock = Factory.CreateProportionalDock();
        dock.Id = obj["id"]?.GetValue<string>() ?? string.Empty;
        dock.Orientation = obj["orientation"]?.GetValue<string>() == "horizontal"
            ? Orientation.Horizontal
            : Orientation.Vertical;
        if (obj["proportion"] is JsonValue pv && pv.TryGetValue(out double prop))
            dock.Proportion = prop;

        var children = RestoreChildren(obj);
        dock.VisibleDockables = Factory.CreateList<IDockable>(children.ToArray());
        return dock;
    }

    private IToolDock RestoreToolDock(JsonObject obj)
    {
        var id = obj["id"]?.GetValue<string>() ?? string.Empty;
        var alignment = obj["alignment"]?.GetValue<string>() is { } alignStr
            ? ParseAlignment(alignStr)
            : Alignment.Unset;
        var proportion = obj["proportion"] is JsonValue pv && pv.TryGetValue(out double p) ? p : double.NaN;
        var minWidth = obj["minWidth"] is JsonValue mwVal && mwVal.TryGetValue(out double mw) ? mw : 0.0;
        var minHeight = obj["minHeight"] is JsonValue mhVal && mhVal.TryGetValue(out double mh) ? mh : 0.0;
        var dock = Factory.CreateStyledToolDock(id, alignment, proportion, minWidth, minHeight);

        var dockables = new List<IDockable>();
        int activeDockableIndex = -1;
        if (obj["activeDockableIndex"] is JsonValue aiVal)
            aiVal.TryGetValue(out activeDockableIndex);

        if (obj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsArray)
        {
            foreach (var toolNode in toolsArray)
            {
                if (toolNode is not JsonObject toolObj) continue;
                var restored = RestoreNode(toolObj);
                if (restored is not null)
                    dockables.Add(restored);
            }
        }

        dock.VisibleDockables = Factory.CreateList<IDockable>(dockables.ToArray());
        if (activeDockableIndex >= 0 && activeDockableIndex < dockables.Count)
        {
            var active = dockables[activeDockableIndex];
            dock.ActiveDockable = active;
            if (active is BeutlToolDockable btd)
            {
                btd.IsActive = true;
                btd.ToolContext.IsSelected.Value = true;
            }
        }
        else if (dockables.Count > 0)
        {
            dock.ActiveDockable = dockables[0];
        }

        return dock;
    }

    private BeutlToolDockable? RestoreBeutlTool(JsonObject obj)
    {
        if (obj["extension"] is not JsonObject extObj || !extObj.TryGetDiscriminator(out Type? extType))
            return null;

        var extension = _editViewModel.ExtensionProvider.AllExtensions
            .FirstOrDefault(x => x.GetType() == extType) as ToolTabExtension;
        if (extension is null) return null;

        bool created = extension.TryCreateContext(_editViewModel, out IToolContext? ctx);
        if (!created || ctx is null)
        {
            if (ctx is not null)
                _ = DisposeContextOnceAsync(ctx);
            return null;
        }

        BeutlToolDockable? dockable = null;
        try
        {
            if (!_restoringArrangementOnly)
            {
                try
                {
                    ctx.ReadFromJson(obj);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to restore tool state for '{ToolType}' ({SceneId})",
                        extType.FullName,
                        _sceneId);
                }
            }

            dockable = new BeutlToolDockable(ctx, _editViewModel);
            // Registered before anything that can throw — including the id parse just below — so a
            // failure anywhere after construction can still dispose it.
            _restoredTools?.Add(dockable);

            if (obj["id"] is JsonValue idValue
                && idValue.TryGetValue(out string? savedId)
                && savedId is { Length: > 0 })
            {
                dockable.Id = savedId;
            }

            return dockable;
        }
        catch
        {
            if (dockable is null)
                _ = DisposeContextOnceAsync(ctx);
            throw;
        }
    }

    private PlayerToolDockable? RestorePlayerDockable()
    {
        return new PlayerToolDockable(_editViewModel.Player, Strings.Preview);
    }

    private List<IDockable> RestoreChildren(JsonObject obj)
    {
        var result = new List<IDockable>();
        if (!obj.TryGetPropertyValue("children", out var childrenNode) || childrenNode is not JsonArray childrenArray)
            return result;

        foreach (var childNode in childrenArray)
        {
            if (childNode is not JsonObject childObj) continue;
            var restored = RestoreNode(childObj);
            if (restored is not null)
                result.Add(restored);
        }

        return result;
    }

    private static Alignment ParseAlignment(string value) => value switch
    {
        "left" => Alignment.Left,
        "right" => Alignment.Right,
        "bottom" => Alignment.Bottom,
        "top" => Alignment.Top,
        _ => Alignment.Unset,
    };
}
