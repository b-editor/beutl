using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Inpc;

namespace Beutl.ViewModels.Dock;

public class BeutlDockFactory(EditViewModel editViewModel) : Factory
{
    private IRootDock? _rootDock;
    private PlayerToolDockable? _playerDockable;

    public IToolDock? LeftDock { get; private set; }

    public IToolDock? RightDock { get; private set; }

    public IToolDock? BottomDock { get; private set; }

    public override IRootDock CreateLayout()
    {
        // Left sidebar
        var leftDock = CreateToolDock();
        leftDock.Id = "Dock.Left";
        leftDock.Proportion = 0.25;
        leftDock.Alignment = Alignment.Left;
        leftDock.GripMode = GripMode.Hidden;
        leftDock.AutoHide = false;
        leftDock.VisibleDockables = CreateList<IDockable>();
        leftDock.MinWidth = 100;
        leftDock.MinHeight = 100;
        LeftDock = leftDock;

        // Player
        _playerDockable = new PlayerToolDockable(editViewModel.Player, "Preview");
        var playerDock = CreateToolDock();
        playerDock.Id = "Dock.Player";
        playerDock.Proportion = 0.5;
        playerDock.Alignment = Alignment.Unset;
        playerDock.GripMode = GripMode.Hidden;
        playerDock.AutoHide = false;
        playerDock.VisibleDockables = CreateList<IDockable>(_playerDockable);
        playerDock.ActiveDockable = _playerDockable;
        playerDock.MinWidth = 100;
        playerDock.MinHeight = 100;

        // Right sidebar
        var rightDock = CreateToolDock();
        rightDock.Id = "Dock.Right";
        rightDock.Proportion = 0.25;
        rightDock.Alignment = Alignment.Right;
        rightDock.GripMode = GripMode.Hidden;
        rightDock.AutoHide = false;
        rightDock.VisibleDockables = CreateList<IDockable>();
        rightDock.MinWidth = 100;
        rightDock.MinHeight = 100;
        RightDock = rightDock;

        var topDock = CreateProportionalDock();
        topDock.Id = "Dock.Top";
        topDock.Proportion = 0.5;
        topDock.Orientation = Orientation.Horizontal;
        topDock.VisibleDockables = CreateList<IDockable>(
            leftDock,
            CreateProportionalDockSplitter(),
            playerDock,
            CreateProportionalDockSplitter(),
            rightDock);

        // Bottom tool dock (for timeline etc.)
        var bottomDock = CreateToolDock();
        bottomDock.Id = "Dock.Bottom";
        bottomDock.Proportion = 0.5;
        bottomDock.Alignment = Alignment.Bottom;
        bottomDock.GripMode = GripMode.Hidden;
        bottomDock.AutoHide = false;
        bottomDock.VisibleDockables = CreateList<IDockable>();
        bottomDock.MinWidth = 100;
        bottomDock.MinHeight = 100;
        BottomDock = bottomDock;

        // Top-level horizontal split
        var root = CreateProportionalDock();
        root.Id = "Dock.Root";
        root.Orientation = Orientation.Vertical;
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(
            topDock,
            CreateProportionalDockSplitter(),
            bottomDock);

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
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

        DockableClosed += OnDockableClosed;
    }

    public BeutlToolDockable? AddTool(IToolContext context, IToolDock? target = null, bool activate = true)
    {
        var zone = target ?? FindFocusedToolDock() ?? FindFirstToolDock();
        if (zone is null) return null;

        var dockable = new BeutlToolDockable(context, editViewModel);
        AddDockable(zone, dockable);
        if (activate)
        {
            SetActiveDockable(dockable);
            if (_rootDock is not null)
                SetFocusedDockable(_rootDock, dockable);
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

    public IRootDock? RootDock => _rootDock;

    public PlayerToolDockable? PlayerDockable => _playerDockable;

    internal void SetRootDock(IRootDock rootDock)
    {
        _rootDock = rootDock;
        // Try to locate named docks from the restored layout
        LeftDock = FindById("Dock.Left") as IToolDock;
        RightDock = FindById("Dock.Right") as IToolDock;
        BottomDock = FindById("Dock.Bottom") as IToolDock;
        _playerDockable = Traverse(rootDock).OfType<PlayerToolDockable>().FirstOrDefault();
    }

    private IToolDock? FindFocusedToolDock()
    {
        if (_rootDock is null) return null;
        foreach (var d in Traverse(_rootDock))
        {
            if (d is IToolDock toolDock && toolDock.ActiveDockable is BeutlToolDockable { ToolContext.IsSelected.Value: true })
                return toolDock;
        }
        return null;
    }

    internal IToolDock? FindFirstToolDock()
    {
        if (_rootDock is null) return null;
        foreach (var d in Traverse(_rootDock))
        {
            if (d is IToolDock toolDock && toolDock.Id != "Dock.Player")
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
}
