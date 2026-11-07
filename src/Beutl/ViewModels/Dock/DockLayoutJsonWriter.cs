using System.Text.Json.Nodes;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.ViewModels.Dock;

// Writes a dock layout in the shape DockLayoutJsonReader restores.
internal static class DockLayoutJsonWriter
{
    public static JsonObject SaveNode(IDockable node, bool includeToolState = true)
    {
        return node switch
        {
            IRootDock root => SaveRootDock(root, includeToolState),
            IProportionalDockSplitter => new JsonObject { ["$type"] = "splitter" },
            IProportionalDock prop => SaveProportionalDock(prop, includeToolState),
            IToolDock toolDock => SaveToolDock(toolDock, includeToolState),
            BeutlToolDockable tool => SaveBeutlTool(tool, includeToolState),
            PlayerToolDockable => new JsonObject { ["$type"] = "player" },
            NewToolTabDockable => new JsonObject { ["$type"] = "new_tab" },
            _ => new JsonObject { ["$type"] = "unknown" },
        };
    }

    private static JsonObject SaveRootDock(IRootDock root, bool includeToolState)
    {
        var obj = new JsonObject
        {
            ["$type"] = "root",
            ["id"] = root.Id
        };

        SaveDockableList(obj, "children", root.VisibleDockables, includeToolState);
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

    private static void SaveDockableList(JsonObject parent, string key, IList<IDockable>? list, bool includeToolState)
    {
        if (list is not { Count: > 0 }) return;
        var array = new JsonArray();
        foreach (var item in list)
            array.Add(SaveNode(item, includeToolState));
        parent[key] = array;
    }

    private static JsonObject SaveProportionalDock(IProportionalDock prop, bool includeToolState)
    {
        var obj = new JsonObject
        {
            ["$type"] = "proportional",
            ["id"] = prop.Id,
            ["orientation"] = prop.Orientation == Orientation.Horizontal ? "horizontal" : "vertical",
        };
        WriteProportion(obj, prop);

        SaveDockableList(obj, "children", prop.VisibleDockables, includeToolState);

        return obj;
    }

    private static JsonObject SaveToolDock(IToolDock toolDock, bool includeToolState)
    {
        var obj = new JsonObject
        {
            ["$type"] = "tool_dock",
            ["id"] = toolDock.Id,
            ["alignment"] = toolDock.Alignment.ToString().ToLowerInvariant(),
            ["minWidth"] = toolDock.MinWidth,
            ["minHeight"] = toolDock.MinHeight,
        };
        WriteProportion(obj, toolDock);

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

    // While a split draws an empty dock collapsed, Dock sets that dock's proportion to 0 and scales
    // the other panes up to fill its room, keeping each pane's share with every pane shown as its
    // collapsed proportion. Saving those shares brings the dock back at its size once it fills
    // again; a saved 0 would leave it no room.
    private static void WriteProportion(JsonObject obj, IDockable dock)
    {
        double proportion = double.IsFinite(dock.CollapsedProportion) && dock.CollapsedProportion > 0
            ? dock.CollapsedProportion
            : dock.Proportion;
        if (!double.IsNaN(proportion))
            obj["proportion"] = proportion;
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
}
