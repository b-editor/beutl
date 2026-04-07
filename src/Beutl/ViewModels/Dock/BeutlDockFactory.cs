using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Inpc;

namespace Beutl.ViewModels.Dock;

public class BeutlDockFactory(EditViewModel editViewModel) : Factory
{
    private readonly Dictionary<string, IToolDock> _zoneLookup = new();
    private IRootDock? _rootDock;
    private PlayerToolDockable? _playerDockable;

    public override IRootDock CreateLayout()
    {
        _zoneLookup.Clear();

        // Left sidebar column
        var leftColumn = CreateProportionalDock();
        leftColumn.Id = DockZoneIds.LeftColumn;
        leftColumn.Proportion = 0.18;
        leftColumn.Orientation = Orientation.Vertical;
        var leftUpperBottom = CreateZone(DockZoneIds.LeftUpperBottom, Alignment.Left);
        leftUpperBottom.Proportion = 0.5;
        var leftLowerTop = CreateZone(DockZoneIds.LeftLowerTop, Alignment.Left);
        leftLowerTop.Proportion = 0.5;
        leftColumn.VisibleDockables = CreateList<IDockable>(
            leftUpperBottom,
            CreateProportionalDockSplitter(),
            leftLowerTop);

        // Center top row (two side tool docks + player)
        var centerTopRow = CreateProportionalDock();
        centerTopRow.Id = DockZoneIds.CenterTopRow;
        centerTopRow.Orientation = Orientation.Horizontal;
        centerTopRow.Proportion = 0.7;
        _playerDockable = new PlayerToolDockable(editViewModel.Player, "Preview");
        var playerZone = CreateZone(DockZoneIds.Player, Alignment.Unset);
        playerZone.Proportion = 0.6;
        playerZone.VisibleDockables = CreateList<IDockable>(_playerDockable);
        playerZone.ActiveDockable = _playerDockable;
        var leftUpperTop = CreateZone(DockZoneIds.LeftUpperTop, Alignment.Left);
        leftUpperTop.Proportion = 0.2;
        var rightUpperTop = CreateZone(DockZoneIds.RightUpperTop, Alignment.Right);
        rightUpperTop.Proportion = 0.2;
        centerTopRow.VisibleDockables = CreateList<IDockable>(
            leftUpperTop,
            CreateProportionalDockSplitter(),
            playerZone,
            CreateProportionalDockSplitter(),
            rightUpperTop);

        // Center bottom row (timeline / horizontal tools)
        var centerBottomRow = CreateProportionalDock();
        centerBottomRow.Id = DockZoneIds.CenterBottomRow;
        centerBottomRow.Orientation = Orientation.Horizontal;
        centerBottomRow.Proportion = 0.3;
        var leftLowerBottom = CreateZone(DockZoneIds.LeftLowerBottom, Alignment.Bottom);
        leftLowerBottom.Proportion = 0.5;
        var rightLowerBottom = CreateZone(DockZoneIds.RightLowerBottom, Alignment.Bottom);
        rightLowerBottom.Proportion = 0.5;
        centerBottomRow.VisibleDockables = CreateList<IDockable>(
            leftLowerBottom,
            CreateProportionalDockSplitter(),
            rightLowerBottom);

        // Center column
        var centerColumn = CreateProportionalDock();
        centerColumn.Id = DockZoneIds.CenterColumn;
        centerColumn.Proportion = 0.64;
        centerColumn.Orientation = Orientation.Vertical;
        centerColumn.VisibleDockables = CreateList<IDockable>(
            centerTopRow,
            CreateProportionalDockSplitter(),
            centerBottomRow);

        // Right sidebar column
        var rightColumn = CreateProportionalDock();
        rightColumn.Id = DockZoneIds.RightColumn;
        rightColumn.Proportion = 0.18;
        rightColumn.Orientation = Orientation.Vertical;
        var rightUpperBottom = CreateZone(DockZoneIds.RightUpperBottom, Alignment.Right);
        rightUpperBottom.Proportion = 0.5;
        var rightLowerTop = CreateZone(DockZoneIds.RightLowerTop, Alignment.Right);
        rightLowerTop.Proportion = 0.5;
        rightColumn.VisibleDockables = CreateList<IDockable>(
            rightUpperBottom,
            CreateProportionalDockSplitter(),
            rightLowerTop);

        // Top-level horizontal split
        var root = CreateProportionalDock();
        root.Id = DockZoneIds.TopHorizontal;
        root.Orientation = Orientation.Horizontal;
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(
            leftColumn,
            CreateProportionalDockSplitter(),
            centerColumn,
            CreateProportionalDockSplitter(),
            rightColumn);

        var rootDock = CreateRootDock();
        rootDock.Id = DockZoneIds.Root;
        rootDock.Title = "Editor";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(root);
        rootDock.ActiveDockable = root;
        rootDock.DefaultDockable = root;

        _rootDock = rootDock;
        return rootDock;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>();
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [DockZoneIds.Root] = () => _rootDock,
        };
        foreach (var (zoneId, zoneDock) in _zoneLookup)
        {
            var captured = zoneDock;
            DockableLocator[zoneId] = () => captured;
        }

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);

        DockableClosed += OnDockableClosed;
    }

    private IToolDock CreateZone(string id, Alignment alignment)
    {
        var dock = CreateToolDock();
        dock.Id = id;
        dock.Alignment = alignment;
        // Hide the separate ToolChrome title bar; tabs act as the header (Unity style).
        dock.GripMode = GripMode.Hidden;
        // Disable auto-hide: tools should only collapse when the user explicitly pins them.
        dock.AutoHide = false;
        dock.VisibleDockables = CreateList<IDockable>();
        // Prevent zones from becoming too small when proportionally split.
        dock.MinWidth = 100;
        dock.MinHeight = 100;
        _zoneLookup[id] = dock;
        return dock;
    }

    public IToolDock? GetZone(string zoneId)
    {
        return _zoneLookup.TryGetValue(zoneId, out var dock) ? dock : null;
    }

    public BeutlToolDockable? AddTool(IToolContext context, bool activate = true)
    {
        var zoneId = DockZoneIds.FromPlacement(context.Placement.Value);
        var zone = GetZone(zoneId) ?? _zoneLookup.Values.FirstOrDefault();
        if (zone is null) return null;

        var dockable = new BeutlToolDockable(context, editViewModel);
        AddDockable(zone, dockable);
        dockable.SyncPlacementFromOwner();
        if (activate)
        {
            SetActiveDockable(dockable);
            SetFocusedDockable(_rootDock!, dockable);
        }
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

    private static IEnumerable<IDockable> Traverse(IDockable node)
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
            if (root.Windows is { } windows)
                foreach (var w in windows)
                    if (w.Layout is not null)
                        foreach (var g in Traverse(w.Layout)) yield return g;
        }
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is BeutlToolDockable dockable)
        {
            dockable.Dispose();
        }
    }

    public IEnumerable<IDockable> EnumerateIdentifiedDocks()
    {
        if (_rootDock is null) yield break;
        foreach (var d in Traverse(_rootDock))
        {
            if (!string.IsNullOrEmpty(d.Id) && (d is IProportionalDock || d is IToolDock))
                yield return d;
        }
    }

    public IDockable? FindById(string id)
    {
        if (_rootDock is null) return null;
        return Traverse(_rootDock).FirstOrDefault(d => d.Id == id);
    }
}
