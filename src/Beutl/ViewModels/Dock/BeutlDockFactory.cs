using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Inpc;

namespace Beutl.ViewModels.Dock;

public class BeutlDockFactory(EditViewModel editViewModel) : Factory
{
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
                    var captured = d;
                    DockableLocator[d.Id] = () => captured;
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

    internal bool OpenToolTab(ToolTabExtension extension, IToolDock target)
    {
        return editViewModel.DockHost.OpenToolTabFromExtension(extension, target);
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
}
