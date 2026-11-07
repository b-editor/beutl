using Beutl.Editor.Components.WebBrowserTab;

using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Inpc;

namespace Beutl.ViewModels.Dock;

public class BeutlDockFactory(EditViewModel editViewModel) : Factory
{
    private sealed class ActivationStamp
    {
        public long Order;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDockable, ActivationStamp> _recentTools = new();
    private long _activationOrder;

    internal long GetActivationOrder(IDockable dockable) =>
        _recentTools.TryGetValue(dockable, out var stamp) ? stamp.Order : 0;

    public override void OnFocusedDockableChanged(IDockable? dockable)
    {
        if (dockable is BeutlToolDockable or PlayerToolDockable or NewToolTabDockable)
            _recentTools.GetValue(dockable, _ => new()).Order = ++_activationOrder;
        base.OnFocusedDockableChanged(dockable);
    }

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
        var frameSize = editViewModel.Scene.FrameSize.Value;
        return frameSize.Height > frameSize.Width;
    }

    private IRootDock CreateLandscapeLayout()
    {
        var leftDock = CreateAnchoredDock(DockAnchor.Left);

        var playerDock = CreatePlayerDock();

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

        return CreateEditorRootDock(root);
    }

    private IRootDock CreatePortraitLayout()
    {
        var playerDock = CreatePlayerDock();

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

        return CreateEditorRootDock(root);
    }

    private IToolDock CreatePlayerDock()
    {
        var playerDockable = new PlayerToolDockable(editViewModel.Player, Strings.Preview);
        var playerDock = CreateAnchoredDock(DockAnchor.Player);
        playerDock.VisibleDockables = CreateList<IDockable>(playerDockable);
        playerDock.ActiveDockable = playerDockable;
        return playerDock;
    }

    private IRootDock CreateEditorRootDock(IDockable root)
    {
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
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);
        RestoreMissingAnchoredDocks();

        DockableLocator = new Dictionary<string, Func<IDockable?>>();

        if (_rootDock is not null)
        {
            foreach (var d in Traverse(_rootDock))
            {
                if (!string.IsNullOrEmpty(d.Id))
                {
                    var captured = d;
                    DockableLocator[d.Id] = () => captured;
                }
            }
        }

        _anchorCacheDirty = true;
    }

    // A dock a tool opens into by default stays in the layout when its last tab goes, so the next
    // tab opened for one of its tools finds it where the user left it, at its size. Dock draws an
    // empty, collapsable dock at no size, so nothing shows meanwhile. A copy floated out to a
    // window under the same id still closes with that window.
    public override void CollapseDock(IDock dock)
    {
        if (dock is IToolDock
            && AnchorFromId(dock.Id) != DockAnchor.None
            && IsInMainLayout(dock))
        {
            return;
        }

        base.CollapseDock(dock);
    }

    // Layouts saved before anchored docks were kept lack the ones that were emptied then. Each
    // goes back empty, so out of sight, where the default layout has it beside the docks that
    // remain. In this order each one finds the ones before it.
    private void RestoreMissingAnchoredDocks()
    {
        if (_rootDock?.VisibleDockables?.FirstOrDefault() is not IProportionalDock rootSplit) return;

        bool portrait = rootSplit.Orientation == Orientation.Horizontal;
        DockAnchor[] order = portrait
            ? [DockAnchor.Bottom, DockAnchor.Left, DockAnchor.Right]
            : [DockAnchor.Left, DockAnchor.Right, DockAnchor.Bottom];
        foreach (DockAnchor anchor in order)
        {
            _anchorCacheDirty = true;
            if (GetAnchoredDock(anchor) is null && FindRestorePlace(anchor, rootSplit, portrait) is { } place)
            {
                InsertBeside(place.Beside, CreateAnchoredDock(anchor), place.Side);
            }
        }

        _anchorCacheDirty = true;
    }

    // Where the default layout has the dock for anchor: beside which dock, on which side. An earlier
    // version also dropped the preview's dock when its tab was moved to another dock, or floated out
    // under the dock's id; the other docks then stand in for it.
    private (IDockable Beside, DockOperation Side)? FindRestorePlace(
        DockAnchor anchor, IProportionalDock rootSplit, bool portrait)
    {
        IDockable? player = FindAnchoredDockInLayout(DockAnchor.Player);
        IDockable? bottom = FindAnchoredDockInLayout(DockAnchor.Bottom);
        IDockable[] rows = PanesOf(rootSplit);
        if (!portrait)
        {
            // Left | preview | right, over the bottom dock.
            if (anchor == DockAnchor.Bottom)
                return rows.Length > 0 ? (rows[^1], DockOperation.Bottom) : null;

            DockOperation side = anchor == DockAnchor.Left ? DockOperation.Left : DockOperation.Right;
            if (player is not null) return (player, side);

            // Without the preview's dock, the dock goes to that end of the row above the bottom dock.
            IDockable? row = rows.FirstOrDefault(pane => !ReferenceEquals(pane, bottom));
            if (row is IProportionalDock { Orientation: Orientation.Horizontal } split
                && PanesOf(split) is { Length: > 0 } panes)
            {
                return (side == DockOperation.Left ? panes[0] : panes[^1], side);
            }

            if (row is not null) return (row, side);
            return bottom is null ? null : (bottom, DockOperation.Top);
        }

        // The preview, beside a column of left | right over the bottom dock.
        IDockable? left = FindAnchoredDockInLayout(DockAnchor.Left);
        IDockable? right = FindAnchoredDockInLayout(DockAnchor.Right);
        if (anchor == DockAnchor.Left && right is not null) return (right, DockOperation.Left);
        if (anchor == DockAnchor.Right && left is not null) return (left, DockOperation.Right);
        if (anchor != DockAnchor.Bottom && bottom is not null) return (bottom, DockOperation.Top);
        if (anchor == DockAnchor.Bottom
            && ChildHolding(rootSplit, left ?? right) is { } tools
            && !ReferenceEquals(tools, ChildHolding(rootSplit, player)))
        {
            return (tools, DockOperation.Bottom);
        }

        // Without the preview's dock, the column starts at the right end.
        IDockable? beside = player ?? (rows.Length > 0 ? rows[^1] : null);
        return beside is null ? null : (beside, DockOperation.Right);
    }

    private IDockable? FindAnchoredDockInLayout(DockAnchor anchor)
    {
        return GetAnchoredDock(anchor) is { } dock && IsInMainLayout(dock)
            ? dock
            : null;
    }

    private bool IsInMainLayout(IDockable dockable)
    {
        return ReferenceEquals(FindRoot(dockable, _ => true), _rootDock);
    }

    private static IDockable[] PanesOf(IDock split)
    {
        return split.VisibleDockables?.Where(d => d is not IProportionalDockSplitter).ToArray() ?? [];
    }

    // The child of split that is, or holds, dockable.
    private static IDockable? ChildHolding(IDock split, IDockable? dockable)
    {
        for (IDockable? node = dockable; node is not null; node = node.Owner)
        {
            if (ReferenceEquals(node.Owner, split)) return node;
        }

        return null;
    }

    // Puts dock beside the given dockable: in the split holding it when that split runs the right
    // way, else in a new split that takes its place. The dock gets its default share, and the other
    // panes keep their ratios.
    private void InsertBeside(IDockable beside, IToolDock dock, DockOperation side)
    {
        if (beside.Owner is not IDock { VisibleDockables: { } siblings } owner
            || !IsInMainLayout(beside))
        {
            return;
        }

        int index = siblings.IndexOf(beside);
        Orientation orientation = side is DockOperation.Left or DockOperation.Right
            ? Orientation.Horizontal
            : Orientation.Vertical;
        if (owner is not IProportionalDock split || split.Orientation != orientation)
        {
            split = CreateProportionalDock();
            split.Orientation = orientation;
            split.Proportion = beside.Proportion;
            split.CollapsedProportion = beside.CollapsedProportion;
            split.VisibleDockables = CreateList<IDockable>();
            beside.Proportion = double.NaN;
            beside.CollapsedProportion = double.NaN;
            RemoveVisibleDockableAt(owner, index);
            InsertDockable(owner, split, index);
            if (ReferenceEquals(owner.ActiveDockable, beside)) owner.ActiveDockable = split;
            if (ReferenceEquals(owner.DefaultDockable, beside)) owner.DefaultDockable = split;
            AddDockable(split, beside);
            index = 0;
        }

        InsertDockable(split, dock, side is DockOperation.Left or DockOperation.Top ? index : index + 1);
        InsertDockable(split, CreateProportionalDockSplitter(), index + 1);
        DockSplitProportion.Apply(dock, dock.Proportion);
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

    /// <summary>
    /// Finds a dock holding a tab of a tool that opens in <paramref name="anchor"/>'s dock by default:
    /// that dock when it holds one, else the dock of the one used last.
    /// </summary>
    /// <remarks>The user may have moved such tools out of their dock.</remarks>
    internal IToolDock? FindDockOfAnchoredTools(DockAnchor anchor)
    {
        if (anchor == DockAnchor.None) return null;

        IToolDock? anchored = GetAnchoredDock(anchor);
        IToolDock? latest = null;
        long latestOrder = -1;
        foreach (BeutlToolDockable tool in EnumerateTools())
        {
            if (tool.Owner is not IToolDock dock || tool.ToolContext.Extension.DefaultAnchor != anchor) continue;
            if (ReferenceEquals(dock, anchored)) return dock;

            long order = GetActivationOrder(tool);
            if (order > latestOrder)
            {
                latest = dock;
                latestOrder = order;
            }
        }

        return latest;
    }

    public BeutlToolDockable? AddTool(IToolContext context, IToolDock? target = null, bool activate = true)
    {
        var zone = target ?? GetAnchoredDock(DockAnchor.Left) ?? FindFirstToolDock();
        if (zone is null) return null;

        var dockable = new BeutlToolDockable(context, editViewModel);
        AddDockable(zone, dockable);
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

    internal Beutl.Api.Services.ExtensionProvider ExtensionProvider => editViewModel.ExtensionProvider;

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

    internal bool OpenToolTab(ToolTabExtension extension, IToolDock target)
    {
        return editViewModel.DockHost.OpenToolTabFromExtension(extension, target);
    }

    /// <summary>Opens an empty tab in <paramref name="target"/> for the user to pick a tool from.</summary>
    internal NewToolTabDockable OpenNewToolTab(IToolDock target)
    {
        var dockable = new NewToolTabDockable { FocusOnShow = true };
        AddDockable(target, dockable);
        SetActiveDockable(dockable);
        SetFocusedDockable(target, dockable);
        _anchorCacheDirty = true;
        return dockable;
    }

    internal bool ReplaceNewToolTab(NewToolTabDockable newTab, ToolTabExtension extension)
    {
        return editViewModel.DockHost.ReplaceNewToolTab(newTab, extension);
    }

    // Takes the empty tab's place: same dock, same position in the strip.
    internal BeutlToolDockable? ReplaceNewToolTab(NewToolTabDockable newTab, IToolContext context)
    {
        if (newTab.Owner is not IDock { VisibleDockables: { } siblings } zone) return null;
        int index = siblings.IndexOf(newTab);
        if (index < 0) return null;

        var dockable = new BeutlToolDockable(context, editViewModel);
        // Insert before removing, so the dock never empties and collapses in between.
        InsertDockable(zone, dockable, index);
        SetActiveDockable(dockable);
        SetFocusedDockable(zone, dockable);
        RemoveDockable(newTab, collapse: false);
        _anchorCacheDirty = true;
        return dockable;
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
        if (dockable is null) return;
        base.CloseDockable(dockable);

        if (dockable is BeutlToolDockable beutlToolDockable)
        {
            beutlToolDockable.Dispose();
        }
    }

    public override void RemoveDockable(IDockable dockable, bool collapse)
    {
        _anchorCacheDirty = true;
        base.RemoveDockable(dockable, collapse);
    }

    public override void SplitToWindow(
        IDock dock,
        IDockable dockable,
        double x,
        double y,
        double width,
        double height,
        DockWindowOptions? options)
    {
        using CompositeDisposable reparentingScopes = BeginToolContentReparenting(dockable);
        base.SplitToWindow(dock, dockable, x, y, width, height, options);
    }

    public override void FloatAllDockables(IDockable dockable, DockWindowOptions? options)
    {
        IDockable reparentingRoot = dockable.Owner is IDock owner ? owner : dockable;
        using CompositeDisposable reparentingScopes = BeginToolContentReparenting(reparentingRoot);
        base.FloatAllDockables(dockable, options);
    }

    public override void MoveDockable(
        IDock sourceDock,
        IDock targetDock,
        IDockable sourceDockable,
        IDockable? targetDockable)
    {
        if (ReferenceEquals(sourceDock, targetDock))
        {
            base.MoveDockable(sourceDock, targetDock, sourceDockable, targetDockable);
            return;
        }

        using CompositeDisposable reparentingScopes = BeginToolContentReparenting(sourceDockable);
        base.MoveDockable(sourceDock, targetDock, sourceDockable, targetDockable);
    }

    public override void SwapDockable(
        IDock sourceDock,
        IDock targetDock,
        IDockable sourceDockable,
        IDockable targetDockable)
    {
        if (ReferenceEquals(sourceDock, targetDock))
        {
            base.SwapDockable(sourceDock, targetDock, sourceDockable, targetDockable);
            return;
        }

        using CompositeDisposable reparentingScopes =
            BeginToolContentReparenting(sourceDockable, targetDockable);
        base.SwapDockable(sourceDock, targetDock, sourceDockable, targetDockable);
    }

    internal static CompositeDisposable BeginToolContentReparenting(params IDockable?[] dockables)
    {
        var result = new CompositeDisposable();
        try
        {
            foreach (IDockable? dockable in dockables)
            {
                AddReparentingScopes(dockable, result);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void AddReparentingScopes(IDockable? dockable, CompositeDisposable result)
    {
        if (dockable is BeutlToolDockable { ToolContent: IWebViewReparentingContent content }
            && content.BeginReparenting() is { } reparentingScope)
        {
            result.Add(reparentingScope);
        }

        if (dockable is IDock { VisibleDockables: { } children })
        {
            foreach (IDockable child in children)
            {
                AddReparentingScopes(child, result);
            }
        }
    }
}
