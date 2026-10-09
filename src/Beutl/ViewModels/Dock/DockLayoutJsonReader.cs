using System.Text.Json.Nodes;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels.Dock;

// Restores a dock layout written by DockLayoutJsonWriter. A reader serves a single restore walk.
internal sealed class DockLayoutJsonReader
{
    private readonly EditViewModel _editViewModel;
    private readonly ILogger _logger;
    private readonly string _sceneId;

    // Collects the tools built so far, so the caller can dispose them when the walk fails midway.
    private readonly List<BeutlToolDockable>? _restoredTools;

    // Set while restoring an arrangement-only payload (a saved layout). Such a payload deliberately
    // omits tool state, so handing it to IToolContext.ReadFromJson would feed every reader a
    // document missing the fields its writer produces.
    private readonly bool _restoringArrangementOnly;

    public DockLayoutJsonReader(
        BeutlDockFactory factory,
        EditViewModel editViewModel,
        ILogger logger,
        string sceneId,
        List<BeutlToolDockable>? restoredTools,
        bool restoringArrangementOnly)
    {
        Factory = factory;
        _editViewModel = editViewModel;
        _logger = logger;
        _sceneId = sceneId;
        _restoredTools = restoredTools;
        _restoringArrangementOnly = restoringArrangementOnly;
    }

    private BeutlDockFactory Factory { get; }

    public IDockable? RestoreNode(JsonObject obj)
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
            "new_tab" => new NewToolTabDockable(),
            _ => null,
        };
    }

    private IRootDock RestoreRootDock(JsonObject obj)
    {
        var rootDock = Factory.CreateRootDock();
        rootDock.Id = obj["id"]?.GetValue<string>() ?? DockIds.Root;
        rootDock.Title = "Editor";
        rootDock.IsCollapsable = false;

        var children = RestoreNodes(obj, "children");
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

                if (!BeutlDockFactory.Traverse(layout).Any(i => i is BeutlToolDockable or PlayerToolDockable or NewToolTabDockable))
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
        List<IDockable> list = RestoreNodes(obj, key);
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

        var children = RestoreNodes(obj, "children");
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

        int activeDockableIndex = -1;
        if (obj["activeDockableIndex"] is JsonValue aiVal)
            aiVal.TryGetValue(out activeDockableIndex);

        List<IDockable> dockables = RestoreNodes(obj, "tools");

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

        if (!extension.TryCreateContext(_editViewModel, out IToolContext? ctx)) return null;

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

        var dockable = new BeutlToolDockable(ctx, _editViewModel);
        // Registered before anything that can throw — including the id parse just below — so a
        // failure anywhere after construction can still dispose it.
        _restoredTools?.Add(dockable);

        if (obj["id"] is JsonValue idValue
            && idValue.TryGetValue(out string? savedId)
            && savedId.Length > 0)
        {
            dockable.Id = savedId;
        }

        return dockable;
    }

    private PlayerToolDockable? RestorePlayerDockable()
    {
        return new PlayerToolDockable(_editViewModel.Player, Strings.Preview);
    }

    private List<IDockable> RestoreNodes(JsonObject obj, string key)
    {
        var result = new List<IDockable>();
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            return result;

        foreach (var item in array)
        {
            if (item is not JsonObject itemObj) continue;
            var restored = RestoreNode(itemObj);
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
