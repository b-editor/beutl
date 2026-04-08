using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Editor.Components.ElementPropertyTab;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public class DockHostViewModel : IDisposable, IJsonSerializable
{
    private const int DockVersion = 2;
    private readonly string _sceneId;
    private readonly EditViewModel _editViewModel;
    private readonly ILogger _logger = Log.CreateLogger<DockHostViewModel>();

    public DockHostViewModel(string sceneId, EditViewModel editViewModel)
    {
        _sceneId = sceneId;
        _editViewModel = editViewModel;
        Factory = new BeutlDockFactory(editViewModel);
        Layout = Factory.CreateLayout();
        Factory.InitLayout(Layout);
    }

    public BeutlDockFactory Factory { get; }

    public IRootDock Layout { get; private set; }

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
            var existing = Factory.EnumerateTools().FirstOrDefault(t => t.ToolContext == item);
            if (existing is not null)
            {
                item.IsSelected.Value = true;
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

        var left = Factory.LeftDock ?? Factory.FindFirstToolDock();
        var right = Factory.RightDock ?? left;
        var bottom = Factory.BottomDock ?? left;

        OpenToolTabFromExtension(LibraryTabExtension.Instance, left);
        OpenToolTabFromExtension(FileBrowserTabExtension.Instance, left);
        OpenToolTabFromExtension(OutputTabExtension.Instance, right);
        OpenToolTabFromExtension(ElementPropertyTabExtension.Instance, right);
        OpenToolTabFromExtension(TimelineTabExtension.Instance, bottom);
    }

    private void OpenToolTabFromExtension(ToolTabExtension ext, IToolDock? target)
    {
        if (ext.TryCreateContext(_editViewModel, out IToolContext? tab))
            OpenToolTab(tab, target);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing DockHostViewModel ({SceneId})", _sceneId);
        foreach (var dockable in Factory.EnumerateTools().ToList())
        {
            dockable.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Serialization: recursive tree walker
    // ──────────────────────────────────────────────────────────────

    public void WriteToJson(JsonObject json)
    {
        _logger.LogInformation("Writing DockHostViewModel to JSON ({SceneId})", _sceneId);
        json["_dockVersion"] = DockVersion;
        json["DockLayout"] = SaveNode(Layout);
    }

    public void ReadFromJson(JsonObject json)
    {
        _logger.LogInformation("Reading DockHostViewModel from JSON ({SceneId})", _sceneId);

        if (json.TryGetPropertyValue("_dockVersion", out var vNode) &&
            vNode is JsonValue vVal && vVal.TryGetValue(out int version) &&
            version == DockVersion &&
            json.TryGetPropertyValue("DockLayout", out var layoutNode) &&
            layoutNode is JsonObject layoutObj)
        {
            try
            {
                var restored = RestoreNode(layoutObj);
                if (restored is IRootDock rootDock)
                {
                    Layout = rootDock;
                    Factory.SetRootDock(rootDock);
                    Factory.InitLayout(rootDock);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore dock layout, using defaults ({SceneId})", _sceneId);
            }
        }

        if (!Factory.EnumerateTools().Any())
        {
            OpenDefaultTabs();
        }
    }

    // ── Save ─────────────────────────────────────────────────────

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
            ["id"] = root.Id,
        };

        if (root.VisibleDockables is { Count: > 0 } visible)
        {
            var children = new JsonArray();
            foreach (var child in visible)
                children.Add(SaveNode(child));
            obj["children"] = children;
        }

        if (root.Windows is { Count: > 0 } windows)
        {
            var windowsArray = new JsonArray();
            foreach (var w in windows)
            {
                if (w.Layout is null) continue;
                var wObj = new JsonObject
                {
                    ["layout"] = SaveNode(w.Layout),
                };
                windowsArray.Add(wObj);
            }
            obj["windows"] = windowsArray;
        }

        return obj;
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
            ["_isActive"] = dockable.IsActive,
        };
        obj.WriteDiscriminator(ctx.Extension.GetType());
        ctx.WriteToJson(obj);
        return obj;
    }

    // ── Restore ──────────────────────────────────────────────────

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
        rootDock.Id = obj["id"]?.GetValue<string>() ?? "Root";
        rootDock.Title = "Editor";
        rootDock.IsCollapsable = false;

        var children = RestoreChildren(obj);
        rootDock.VisibleDockables = Factory.CreateList<IDockable>(children.ToArray());
        if (rootDock.VisibleDockables.Count > 0)
        {
            rootDock.ActiveDockable = rootDock.VisibleDockables[0];
            rootDock.DefaultDockable = rootDock.VisibleDockables[0];
        }

        // Restore floating windows
        if (obj.TryGetPropertyValue("windows", out var wNode) && wNode is JsonArray wArray)
        {
            foreach (var wItem in wArray)
            {
                if (wItem is not JsonObject wObj) continue;
                if (!wObj.TryGetPropertyValue("layout", out var layoutNode) || layoutNode is not JsonObject layoutObj) continue;
                var layout = RestoreNode(layoutObj);
                if (layout is null) continue;

                var window = Factory.CreateDockWindow();
                window.Layout = layout as IRootDock ?? CreateWindowRootDock(layout);
                rootDock.Windows ??= Factory.CreateList<IDockWindow>();
                rootDock.Windows.Add(window);
            }
        }

        return rootDock;
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
        var dock = Factory.CreateToolDock();
        dock.Id = obj["id"]?.GetValue<string>() ?? string.Empty;
        dock.GripMode = GripMode.Hidden;
        dock.AutoHide = false;
        dock.MinWidth = 100;
        dock.MinHeight = 100;

        if (obj["alignment"]?.GetValue<string>() is { } alignStr)
            dock.Alignment = ParseAlignment(alignStr);
        if (obj["proportion"] is JsonValue pv && pv.TryGetValue(out double prop))
            dock.Proportion = prop;

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
            dock.ActiveDockable = dockables[activeDockableIndex];
        else if (dockables.Count > 0)
            dock.ActiveDockable = dockables[0];

        return dock;
    }

    private BeutlToolDockable? RestoreBeutlTool(JsonObject obj)
    {
        if (!obj.TryGetDiscriminator(out Type? extType)) return null;

        var extension = ExtensionProvider.Current.AllExtensions
            .FirstOrDefault(x => x.GetType() == extType) as ToolTabExtension;
        if (extension is null) return null;

        if (!extension.TryCreateContext(_editViewModel, out IToolContext? ctx)) return null;

        try { ctx.ReadFromJson(obj); } catch { /* ignored */ }

        var active = false;
        if (obj["_isActive"] is JsonValue activeValue)
            activeValue.TryGetValue(out active);

        var dockable = new BeutlToolDockable(ctx, _editViewModel);
        if (active)
        {
            dockable.IsActive = true;
            ctx.IsSelected.Value = true;
        }
        return dockable;
    }

    private PlayerToolDockable? RestorePlayerDockable()
    {
        return new PlayerToolDockable(_editViewModel.Player, "Preview");
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
