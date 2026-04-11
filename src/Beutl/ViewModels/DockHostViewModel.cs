using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class DockHostViewModel : IDisposable, IJsonSerializable
{
    private const int DockVersion = 2;
    private readonly string _sceneId;
    private readonly EditViewModel _editViewModel;
    private readonly ILogger _logger = Log.CreateLogger<DockHostViewModel>();
    private bool _layoutInitialized;

    public DockHostViewModel(string sceneId, EditViewModel editViewModel)
    {
        _sceneId = sceneId;
        _editViewModel = editViewModel;
        Factory = new BeutlDockFactory(editViewModel);

        var placeholder = Factory.CreateRootDock();
        placeholder.Id = DockIds.Root;
        placeholder.IsCollapsable = false;
        Layout = new ReactivePropertySlim<IRootDock>(placeholder);
    }

    public BeutlDockFactory Factory { get; }

    public ReactivePropertySlim<IRootDock> Layout { get; }

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

    public bool OpenToolTab(IToolContext item)
    {
        return OpenToolTab(item, target: null);
    }

    public bool OpenToolTab(IToolContext item, IToolDock? target)
    {
        _logger.LogInformation("Attempting to open tool tab '{ToolTabName}' ({SceneId})", item.Extension.Name, _sceneId);
        try
        {
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

            var dockable = Factory.AddTool(item, target);
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

    public void CloseToolTab(IToolContext item)
    {
        _logger.LogInformation("Attempting to close tool tab '{ToolName}' ({SceneId})", item.Extension.Name, _sceneId);
        try
        {
            var dockable = Factory.EnumerateTools().FirstOrDefault(t => t.ToolContext == item);
            if (dockable is null)
            {
                item.Dispose();
                return;
            }

            Factory.CloseDockable(dockable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close tool tab '{ToolName}'. ({SceneId})", item.Extension.Name, _sceneId);
        }
    }

    public void OpenDefaultTabs()
    {
        _logger.LogInformation("Opening default tabs ({SceneId})", _sceneId);

        EnsureDefaultLayout();

        var fallback = Factory.GetAnchoredDock(DockAnchor.Left) ?? Factory.FindFirstToolDock();

        var extensions = ExtensionProvider.Current.AllExtensions
            .OfType<ToolTabExtension>()
            .Where(e => e.OpenByDefault)
            .OrderBy(e => (int)e.DefaultAnchor)
            .ThenBy(e => e.DefaultOrder);

        foreach (var ext in extensions)
        {
            var target = Factory.GetAnchoredDock(ext.DefaultAnchor) ?? fallback;
            OpenToolTabFromExtension(ext, target);
        }

        if (Factory.GetAnchoredDock(DockAnchor.Bottom) is { } bottomDock)
            bottomDock.ActiveDockable = bottomDock.VisibleDockables?.FirstOrDefault();

        if (Factory.GetAnchoredDock(DockAnchor.Left) is { } leftDock)
            leftDock.ActiveDockable = leftDock.VisibleDockables?.FirstOrDefault();

        if (Factory.GetAnchoredDock(DockAnchor.Right) is { } rightDock)
            rightDock.ActiveDockable = rightDock.VisibleDockables?.FirstOrDefault();
    }

    private void OpenToolTabFromExtension(ToolTabExtension ext, IToolDock? target)
    {
        if (ext.TryCreateContext(_editViewModel, out IToolContext? tab))
            OpenToolTab(tab, target);
    }

    private void EnsureDefaultLayout()
    {
        if (_layoutInitialized) return;
        var layout = Factory.CreateLayout();
        Factory.InitLayout(layout);
        Layout.Value = layout;
        _layoutInitialized = true;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing DockHostViewModel ({SceneId})", _sceneId);
        foreach (var dockable in Factory.EnumerateTools().ToList())
        {
            Factory.CloseDockable(dockable);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        _logger.LogInformation("Writing DockHostViewModel to JSON ({SceneId})", _sceneId);
        json["_dockVersion"] = DockVersion;
        json["DockLayout"] = SaveNode(Layout.Value);
    }

    public void ReadFromJson(JsonObject json)
    {
        _logger.LogInformation("Reading DockHostViewModel from JSON ({SceneId})", _sceneId);

        var hasVersion = json.TryGetPropertyValue("_dockVersion", out var vNode) &&
            vNode is JsonValue vVal && vVal.TryGetValue(out int version) &&
            version == DockVersion;

        if (hasVersion &&
            json.TryGetPropertyValue("DockLayout", out var layoutNode) &&
            layoutNode is JsonObject layoutObj)
        {
            try
            {
                var restored = RestoreNode(layoutObj);
                if (restored is IRootDock rootDock)
                {
                    Factory.SetRootDock(rootDock);
                    Factory.InitLayout(rootDock);
                    Layout.Value = rootDock;
                    _layoutInitialized = true;
                }
                else
                {
                    ResetToDefaultLayout("restored root dock was not an IRootDock");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore dock layout, using defaults ({SceneId})", _sceneId);
                ResetToDefaultLayout("restore threw an exception");
            }
        }
        else
        {
            _logger.LogInformation(
                "Dock layout version missing or mismatched, initializing defaults ({SceneId})",
                _sceneId);
        }

        if (!Factory.EnumerateTools().Any())
        {
            OpenDefaultTabs();
        }
    }

    public void ResetLayout()
    {
        ResetToDefaultLayout("user requested");
        OpenDefaultTabs();
    }

    private void ResetToDefaultLayout(string reason)
    {
        _logger.LogWarning("Resetting dock layout to defaults ({Reason}, {SceneId})", reason, _sceneId);
        foreach (var tool in Factory.EnumerateTools().ToList())
        {
            try
            {
                Factory.CloseDockable(tool);
            }
            catch
            {
            }
        }

        _layoutInitialized = false;
        EnsureDefaultLayout();
    }

    private JsonObject SaveNode(IDockable node)
    {
        return node switch
        {
            IRootDock root => SaveRootDock(root),
            IProportionalDockSplitter => new JsonObject { ["$type"] = "splitter" },
            IProportionalDock prop => SaveProportionalDock(prop),
            IToolDock toolDock => SaveToolDock(toolDock),
            BeutlToolDockable tool => SaveBeutlTool(tool),
            PlayerToolDockable => new JsonObject { ["$type"] = "player" },
            _ => new JsonObject { ["$type"] = "unknown" },
        };
    }

    private JsonObject SaveRootDock(IRootDock root)
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
                children.Add(SaveNode(child));
            obj["children"] = children;
        }

        SaveDockableList(obj, "hidden", root.HiddenDockables);
        SaveDockableList(obj, "leftPinned", root.LeftPinnedDockables);
        SaveDockableList(obj, "rightPinned", root.RightPinnedDockables);
        SaveDockableList(obj, "topPinned", root.TopPinnedDockables);
        SaveDockableList(obj, "bottomPinned", root.BottomPinnedDockables);

        if (root.Windows is { Count: > 0 } windows)
        {
            var windowsArray = new JsonArray();
            foreach (var w in windows)
            {
                if (w.Layout is null) continue;
                var wObj = new JsonObject
                {
                    ["layout"] = SaveNode(w.Layout),
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

    private void SaveDockableList(JsonObject parent, string key, IList<IDockable>? list)
    {
        if (list is not { Count: > 0 }) return;
        var array = new JsonArray();
        foreach (var item in list)
            array.Add(SaveNode(item));
        parent[key] = array;
    }

    private JsonObject SaveProportionalDock(IProportionalDock prop)
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
                children.Add(SaveNode(child));
            obj["children"] = children;
        }

        return obj;
    }

    private JsonObject SaveToolDock(IToolDock toolDock)
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
                tools.Add(SaveNode(child));
                if (child == toolDock.ActiveDockable)
                    activeDockableIndex = i;
            }

            obj["tools"] = tools;
            if (activeDockableIndex >= 0)
                obj["activeDockableIndex"] = activeDockableIndex;
        }

        return obj;
    }

    private static JsonObject SaveBeutlTool(BeutlToolDockable dockable)
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
        ctx.WriteToJson(obj);
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

        var extension = ExtensionProvider.Current.AllExtensions
            .FirstOrDefault(x => x.GetType() == extType) as ToolTabExtension;
        if (extension is null) return null;

        if (!extension.TryCreateContext(_editViewModel, out IToolContext? ctx)) return null;

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

        var dockable = new BeutlToolDockable(ctx, _editViewModel);
        if (obj["id"]?.GetValue<string>() is { Length: > 0 } savedId)
            dockable.Id = savedId;
        return dockable;
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
