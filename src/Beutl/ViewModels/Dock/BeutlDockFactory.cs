using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Inpc;

namespace Beutl.ViewModels.Dock;

internal class BeutlDockFactory(EditViewModel editViewModel) : Factory
{
    internal Func<BeutlToolDockable, Task>? DisposalTracker { get; set; }
    internal Action<BeutlToolDockable>? AfterToolAttach { get; set; }
    internal Action? LayoutMutated { get; set; }
    private readonly record struct AnchorDefinition(
        string Id,
        Alignment Alignment,
        double Proportion,
        double MinWidth,
        double MinHeight);

    private static readonly Dictionary<DockAnchor, AnchorDefinition> s_anchorDefinitions = new()
    {
        [DockAnchor.Left] = new(DockIds.Left, Alignment.Left, 0.25, 160.0, 0.0),
        [DockAnchor.Right] = new(DockIds.Right, Alignment.Right, 0.25, 160.0, 0.0),
        [DockAnchor.Bottom] = new(DockIds.Bottom, Alignment.Bottom, 0.5, 0.0, 120.0),
        [DockAnchor.Player] = new(DockIds.Player, Alignment.Unset, 0.5, 0.0, 0.0),
    };

    private readonly Dictionary<DockAnchor, IToolDock?> _anchorCache = new();
    private IRootDock? _rootDock;
    private bool _anchorCacheDirty = true;

    public override IRootDock CreateLayout()
    {
        return IsPortraitScene()
            ? CreatePortraitLayout()
            : CreateLandscapeLayout();
    }

    private bool IsPortraitScene()
    {
        var frameSize = editViewModel.Scene.FrameSize;
        return frameSize.Height > frameSize.Width;
    }

    private IRootDock CreateLandscapeLayout()
    {
        var leftDock = CreateAnchoredDock(DockAnchor.Left);

        var playerDockable = new PlayerToolDockable(editViewModel.Player, Strings.Preview);
        var playerDock = CreateAnchoredDock(DockAnchor.Player);
        playerDock.VisibleDockables = CreateList<IDockable>(playerDockable);
        playerDock.ActiveDockable = playerDockable;

        var rightDock = CreateAnchoredDock(DockAnchor.Right);

        var topDock = CreateProportionalDock();
        topDock.Id = DockIds.TopSplit;
        topDock.Proportion = 0.5;
        topDock.Orientation = Orientation.Horizontal;
        topDock.VisibleDockables = CreateList<IDockable>(
            leftDock,
            CreateProportionalDockSplitter(),
            playerDock,
            CreateProportionalDockSplitter(),
            rightDock);

        var bottomDock = CreateAnchoredDock(DockAnchor.Bottom);

        var root = CreateProportionalDock();
        root.Id = DockIds.RootSplit;
        root.Orientation = Orientation.Vertical;
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(
            topDock,
            CreateProportionalDockSplitter(),
            bottomDock);

        var rootDock = CreateRootDock();
        rootDock.Id = DockIds.Root;
        rootDock.Title = "Editor";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(root);
        rootDock.ActiveDockable = root;
        rootDock.DefaultDockable = root;

        _rootDock = rootDock;
        _anchorCacheDirty = true;
        return rootDock;
    }

    private IRootDock CreatePortraitLayout()
    {
        var playerDockable = new PlayerToolDockable(editViewModel.Player, Strings.Preview);
        var playerDock = CreateAnchoredDock(DockAnchor.Player);
        playerDock.VisibleDockables = CreateList<IDockable>(playerDockable);
        playerDock.ActiveDockable = playerDockable;

        var leftDock = CreateAnchoredDock(DockAnchor.Left);
        var rightDock = CreateAnchoredDock(DockAnchor.Right);

        // Library / file browser | properties
        var toolsRow = CreateProportionalDock();
        toolsRow.Id = DockIds.ToolsRow;
        toolsRow.Orientation = Orientation.Horizontal;
        toolsRow.VisibleDockables = CreateList<IDockable>(
            leftDock,
            CreateProportionalDockSplitter(),
            rightDock);

        var bottomDock = CreateAnchoredDock(DockAnchor.Bottom);

        var rightColumn = CreateProportionalDock();
        rightColumn.Id = DockIds.RightColumn;
        rightColumn.Orientation = Orientation.Vertical;
        // Preview : tools column = 1 : 2 (the dock panel normalizes proportions).
        playerDock.Proportion = 0.5;
        rightColumn.Proportion = 1.0;
        rightColumn.VisibleDockables = CreateList<IDockable>(
            toolsRow,
            CreateProportionalDockSplitter(),
            bottomDock);

        var root = CreateProportionalDock();
        root.Id = DockIds.RootSplit;
        root.Orientation = Orientation.Horizontal;
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(
            playerDock,
            CreateProportionalDockSplitter(),
            rightColumn);

        var rootDock = CreateRootDock();
        rootDock.Id = DockIds.Root;
        rootDock.Title = "Editor";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(root);
        rootDock.ActiveDockable = root;
        rootDock.DefaultDockable = root;

        _rootDock = rootDock;
        _anchorCacheDirty = true;
        return rootDock;
    }

    public IToolDock CreateAnchoredDock(DockAnchor anchor)
    {
        if (!s_anchorDefinitions.TryGetValue(anchor, out var def))
            throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unsupported DockAnchor.");
        return CreateStyledToolDock(def.Id, def.Alignment, def.Proportion, def.MinWidth, def.MinHeight);
    }

    internal IToolDock CreateStyledToolDock(string id, Alignment alignment, double proportion, double minWidth, double minHeight)
    {
        var dock = CreateToolDock();
        dock.Id = id;
        dock.Alignment = alignment;
        dock.Proportion = proportion;
        dock.GripMode = GripMode.Hidden;
        dock.AutoHide = false;
        dock.MinWidth = minWidth;
        dock.MinHeight = minHeight;
        dock.VisibleDockables = CreateList<IDockable>();
        return dock;
    }

    public override void InitLayout(IDockable layout)
    {
        DockableLocator = new Dictionary<string, Func<IDockable?>>();

        if (_rootDock is not null)
        {
            foreach (var d in Traverse(_rootDock))
            {
                if (!string.IsNullOrEmpty(d.Id))
                {
                    var weak = new WeakReference<IDockable>(d);
                    DockableLocator[d.Id] = () => weak.TryGetTarget(out IDockable? target) ? target : null;
                }
            }
        }

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);

        _anchorCacheDirty = true;
    }

    public IToolDock? GetAnchoredDock(DockAnchor anchor)
    {
        if (anchor == DockAnchor.None) return null;
        if (_anchorCacheDirty)
            RebuildAnchorCache();
        return _anchorCache.TryGetValue(anchor, out var dock) ? dock : null;
    }

    private void RebuildAnchorCache()
    {
        _anchorCache.Clear();
        _anchorCacheDirty = false;
        if (_rootDock is null) return;

        foreach (var d in Traverse(_rootDock))
        {
            if (d is not IToolDock toolDock) continue;
            var anchor = AnchorFromId(toolDock.Id);
            if (anchor != DockAnchor.None && !_anchorCache.ContainsKey(anchor))
                _anchorCache[anchor] = toolDock;
        }
    }

    private static DockAnchor AnchorFromId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return DockAnchor.None;
        foreach (var (anchor, def) in s_anchorDefinitions)
        {
            if (def.Id == id) return anchor;
        }
        return DockAnchor.None;
    }

    public BeutlToolDockable? AddTool(IToolContext context, IToolDock? target = null, bool activate = true)
    {
        var zone = target ?? GetAnchoredDock(DockAnchor.Left) ?? FindFirstToolDock();
        if (zone is null) return null;

        var dockable = new BeutlToolDockable(context, editViewModel);
        AddDockable(zone, dockable);
        AfterToolAttach?.Invoke(dockable);
        if (activate)
        {
            SetActiveDockable(dockable);
            if (_rootDock is not null)
                SetFocusedDockable(_rootDock, dockable);
        }
        _anchorCacheDirty = true;
        return dockable;
    }

    public IEnumerable<BeutlToolDockable> EnumerateTools()
    {
        if (_rootDock is null) yield break;
        foreach (var d in Traverse(_rootDock))
        {
            if (d is BeutlToolDockable tool) yield return tool;
        }
    }

    internal IEnumerable<ToolTabExtension> EnumerateToolTabExtensions()
    {
        return editViewModel.ExtensionProvider.AllExtensions
            .OfType<ToolTabExtension>()
            .Where(extension => extension.Header is not null)
            .OrderBy(extension => extension.Name);
    }

    internal bool IsToolTabOpen(ToolTabExtension extension)
    {
        return EnumerateTools().Any(tool => tool.ToolContext.Extension == extension);
    }

    internal Task<bool> OpenToolTabAsync(ToolTabExtension extension, IToolDock target)
    {
        return editViewModel.DockHost.OpenToolTabFromExtensionAsync(extension, target);
    }

    internal void SetRootDock(IRootDock rootDock)
    {
        _rootDock = rootDock;
        _anchorCacheDirty = true;
    }

    internal IToolDock? FindFirstToolDock()
    {
        if (_rootDock is null) return null;
        foreach (var d in Traverse(_rootDock))
        {
            if (d is IToolDock toolDock && toolDock.Id != DockIds.Player)
                return toolDock;
        }
        return null;
    }

    internal static IEnumerable<IDockable> Traverse(IDockable node)
    {
        yield return node;
        if (node is IDock dock && dock.VisibleDockables is { } list)
        {
            foreach (var child in list)
                foreach (var grand in Traverse(child))
                    yield return grand;
        }
        if (node is IRootDock root)
        {
            if (root.HiddenDockables is { } hidden)
                foreach (var c in hidden)
                    foreach (var g in Traverse(c)) yield return g;
            // Pinned (auto-hidden) tools live outside VisibleDockables but are still owned by the
            // layout — they are serialized and restored, so enumeration must see them too.
            foreach (var pinned in new[]
                     {
                         root.LeftPinnedDockables, root.RightPinnedDockables,
                         root.TopPinnedDockables, root.BottomPinnedDockables
                     })
            {
                if (pinned is null) continue;
                foreach (var c in pinned)
                    foreach (var g in Traverse(c)) yield return g;
            }
            if (root.Windows is { } windows)
                foreach (var w in windows)
                    if (w.Layout is not null)
                        foreach (var g in Traverse(w.Layout)) yield return g;
        }
    }

    public override void CloseDockable(IDockable? dockable)
    {
        _anchorCacheDirty = true;
        if (dockable is not null)
            TryCloseDockable(dockable);
    }

    internal void DetachDockable(IDockable dockable)
    {
        _anchorCacheDirty = true;
        try { ForceDetachDockable(dockable); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Force-detach failed: {0}", ex.Message); }
    }

    internal bool TryCloseDockable(IDockable dockable)
    {
        if (dockable is null || dockable.Owner is null)
            return false;
        IDock owner = (IDock)dockable.Owner;
        IRootDock? root = FindRoot(dockable, _ => true) ?? _rootDock;
        try
        {
            base.CloseDockable(dockable);
        }
        catch
        {
        }
        if (!IsAttached(dockable, owner, root))
        {
            CleanupDetachedState(dockable, owner, root);
            TrackDisposalIfNeeded(dockable);
            return true;
        }
        return false;
    }

    private void ForceDetachDockable(IDockable dockable)
    {
        IDock? owner = dockable.Owner as IDock;
        IRootDock? root = FindRoot(dockable, _ => true) ?? _rootDock;
        try
        {
            List<IDock> docks = root is null
                ? []
                : Traverse(root).OfType<IDock>()
                    .ToList();
            if (root is not null && !docks.Any(item => ReferenceEquals(item, root)))
                docks.Add(root);
            if (owner is not null && !docks.Any(item => ReferenceEquals(item, owner)))
                docks.Add(owner);

            if (root is not null)
            {
                TryCleanup(() => root.HiddenDockables?.Remove(dockable));
                TryCleanup(() => root.LeftPinnedDockables?.Remove(dockable));
                TryCleanup(() => root.RightPinnedDockables?.Remove(dockable));
                TryCleanup(() => root.TopPinnedDockables?.Remove(dockable));
                TryCleanup(() => root.BottomPinnedDockables?.Remove(dockable));
            }
            foreach (IDock dock in docks)
            {
                TryCleanup(() => dock.VisibleDockables?.Remove(dockable));
                TryCleanup(() =>
                {
                    if (ReferenceEquals(dock.ActiveDockable, dockable))
                    {
                        dock.ActiveDockable = dock.VisibleDockables?.FirstOrDefault(
                            static item => item is not ISplitter);
                    }
                });
            }
        }
        finally
        {
            CleanupDetachedState(dockable, owner, root);
            CleanupEmptyFloatingWindow(root);
        }
    }

    private void CleanupEmptyFloatingWindow(IRootDock? root)
    {
        if (root?.Window is not { Owner: IRootDock } window
            || Traverse(root).Any(static item => item is not IDock && item is not ISplitter))
        {
            return;
        }

        TryCleanup(() => RemoveWindow(window));
        TryCleanup(() => (window.Owner as IRootDock)?.Windows?.Remove(window));
        TryCleanup(() => root.Window = null);
        TryCleanup(() => window.ParentWindow = null);
        TryCleanup(() => window.Owner = null);
        TryCleanup(() => window.Factory = null);
        TryCleanup(() => window.Layout = null);
    }

    private static bool IsAttached(IDockable dockable, IDock owner, IRootDock? root)
        => Contains(owner.VisibleDockables, dockable)
            || Contains(root?.HiddenDockables, dockable)
            || Contains(root?.LeftPinnedDockables, dockable)
            || Contains(root?.RightPinnedDockables, dockable)
            || Contains(root?.TopPinnedDockables, dockable)
            || Contains(root?.BottomPinnedDockables, dockable);

    private static bool Contains(IList<IDockable>? items, IDockable dockable)
    {
        try { return items?.Contains(dockable) == true; }
        catch { return true; }
    }

    private void CleanupDetachedState(IDockable dockable, IDock? owner, IRootDock? root)
    {
        TryCleanup(() =>
        {
            if (owner?.ActiveDockable == dockable)
                owner.ActiveDockable = owner.VisibleDockables?.FirstOrDefault();
        });
        TryCleanup(() =>
        {
            if (root?.FocusedDockable == dockable)
                root.FocusedDockable = owner?.ActiveDockable;
        });
        TryCleanup(() =>
        {
            if (ReferenceEquals(CurrentDockable, dockable))
                OnDockableDeactivated(dockable);
        });
        TryCleanup(() => ToolControls.Remove(dockable));
        TryCleanup(() => DocumentControls.Remove(dockable));
        TryCleanup(() => VisibleDockableControls.Remove(dockable));
        TryCleanup(() => PinnedDockableControls.Remove(dockable));
        TryCleanup(() => TabDockableControls.Remove(dockable));
        TryCleanup(() =>
        {
            string? id = dockable.Id;
            IDictionary<string, Func<IDockable?>>? locatorMap = DockableLocator;
            if (!string.IsNullOrEmpty(id)
                && locatorMap?.TryGetValue(id, out Func<IDockable?>? locate) == true
                && locate is not null
                && ReferenceEquals(locate(), dockable))
            {
                locatorMap.Remove(id);
            }
        });
        TryCleanup(() => dockable.Context = null);
        TryCleanup(() => dockable.Owner = null);
        TryCleanup(() => dockable.OriginalOwner = null);
        TryCleanup(() => dockable.Factory = null);
    }

    private static void TryCleanup(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Dockable cleanup failed: {0}", ex.Message); }
    }

    private void TrackDisposalIfNeeded(IDockable dockable)
    {
        if (dockable is BeutlToolDockable tool)
            TrackDisposal(tool);
    }

    internal Task DisposeDetachedDockable(IDockable dockable)
    {
        if (dockable is not BeutlToolDockable tool)
            return Task.CompletedTask;
        Task task = tool.GetDisposeTask();
        return task;
    }

    private void TrackDisposal(BeutlToolDockable dockable)
    {
        if (DisposalTracker is { } tracker)
        {
            _ = tracker(dockable);
        }
        else
            _ = dockable.GetDisposeTask().ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    public override void RemoveDockable(IDockable dockable, bool collapse)
    {
        _anchorCacheDirty = true;
        base.RemoveDockable(dockable, collapse);
    }

    public override void OnDockableAdded(IDockable? dockable)
    {
        base.OnDockableAdded(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnActiveDockableChanged(IDockable? dockable)
    {
        base.OnActiveDockableChanged(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableRemoved(IDockable? dockable)
    {
        base.OnDockableRemoved(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableMoved(IDockable? dockable)
    {
        base.OnDockableMoved(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableUndocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableUndocked(dockable, operation);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableSwapped(IDockable? dockable)
    {
        base.OnDockableSwapped(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockablePinned(IDockable? dockable)
    {
        base.OnDockablePinned(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableUnpinned(IDockable? dockable)
    {
        base.OnDockableUnpinned(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnDockableRestored(IDockable? dockable)
    {
        base.OnDockableRestored(dockable);
        LayoutMutated?.Invoke();
    }

    public override void OnWindowAdded(IDockWindow? window)
    {
        base.OnWindowAdded(window);
        LayoutMutated?.Invoke();
    }

    public override void OnWindowRemoved(IDockWindow? window)
    {
        base.OnWindowRemoved(window);
        LayoutMutated?.Invoke();
    }

    public override void OnWindowMoveDragEnd(IDockWindow? window)
    {
        base.OnWindowMoveDragEnd(window);
        LayoutMutated?.Invoke();
    }

    public override bool OnWindowMoveDragBegin(IDockWindow? window)
    {
        bool accepted = base.OnWindowMoveDragBegin(window);
        if (accepted)
            LayoutMutated?.Invoke();
        return accepted;
    }

    public override void OnWindowMoveDrag(IDockWindow? window)
    {
        base.OnWindowMoveDrag(window);
        LayoutMutated?.Invoke();
    }
}
