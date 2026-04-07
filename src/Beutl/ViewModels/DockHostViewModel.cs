using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Editor.Components.ElementPropertyTab;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public class DockHostViewModel : IDisposable, IJsonSerializable
{
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

            var dockable = Factory.AddTool(item);
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
        var tabs = new ToolTabExtension[]
        {
            TimelineTabExtension.Instance,
            OutputTabExtension.Instance,
            ElementPropertyTabExtension.Instance,
            LibraryTabExtension.Instance,
            FileBrowserTabExtension.Instance,
        };
        foreach (var ext in tabs)
        {
            if (ext.TryCreateContext(_editViewModel, out IToolContext? tab))
                OpenToolTab(tab);
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing DockHostViewModel ({SceneId})", _sceneId);
        foreach (var dockable in Factory.EnumerateTools().ToList())
        {
            dockable.Dispose();
        }
    }

    public void WriteToJson(JsonObject json)
    {
        _logger.LogInformation("Writing DockHostViewModel to JSON ({SceneId})", _sceneId);

        // Persist which tool contexts are open (Extension type + per-tool json).
        var toolsArray = new JsonArray();
        foreach (var dockable in Factory.EnumerateTools())
        {
            var ctx = dockable.ToolContext;
            var itemJson = new JsonObject();
            ctx.WriteToJson(itemJson);
            itemJson.WriteDiscriminator(ctx.Extension.GetType());
            itemJson["_zone"] = DockZoneIds.FromPlacement(ctx.Placement.Value);
            itemJson["_isActive"] = dockable.IsActive;
            toolsArray.Add(itemJson);
        }
        json["DockTools"] = toolsArray;

        // Persist proportional split sizes keyed by dock Id.
        var proportions = new JsonObject();
        foreach (var dock in Factory.EnumerateIdentifiedDocks())
        {
            if (!double.IsNaN(dock.Proportion))
                proportions[dock.Id] = dock.Proportion;
        }
        json["DockProportions"] = proportions;
    }

    public void ReadFromJson(JsonObject json)
    {
        _logger.LogInformation("Reading DockHostViewModel from JSON ({SceneId})", _sceneId);

        // Legacy format: keys LeftUpperTop, LeftUpperBottom, ... (from ReDocking era).
        var isLegacy = DockZoneIds.AllToolZones.Any(zoneId =>
        {
            var name = DockZoneIds.ToPlacement(zoneId)?.ToString();
            return name is not null && json.ContainsKey(name);
        });

        if (isLegacy)
        {
            MigrateLegacyLayout(json);
        }
        else if (json.TryGetPropertyValue("DockTools", out var toolsNode) && toolsNode is JsonArray toolsArray)
        {
            RestoreTools(toolsArray);
        }

        if (json.TryGetPropertyValue("DockProportions", out var propNode) && propNode is JsonObject props)
        {
            RestoreProportions(props);
        }
        else if (isLegacy)
        {
            // Legacy had separate Proportion objects; try to infer reasonable splits from them.
            RestoreLegacyProportions(json);
        }

        if (!Factory.EnumerateTools().Any())
        {
            OpenDefaultTabs();
        }
    }

    private void RestoreProportions(JsonObject props)
    {
        foreach (var kvp in props)
        {
            if (kvp.Value is JsonValue v && v.TryGetValue(out double proportion))
            {
                if (Factory.FindById(kvp.Key) is { } dock)
                    dock.Proportion = proportion;
            }
        }
    }

    private void RestoreLegacyProportions(JsonObject json)
    {
        // Legacy keys (approximate mapping):
        //  - LeftRightProportion { Left, Center, Right }    → LeftColumn / CenterColumn / RightColumn
        //  - CenterTopBottomProportion { First, Second }    → CenterTopRow / CenterBottomRow
        if (json["LeftRightProportion"] is JsonObject lrp)
        {
            SetDockProp(DockZoneIds.LeftColumn, lrp, "Left");
            SetDockProp(DockZoneIds.CenterColumn, lrp, "Center");
            SetDockProp(DockZoneIds.RightColumn, lrp, "Right");
        }
        if (json["CenterTopBottomProportion"] is JsonObject ctbp)
        {
            SetDockProp(DockZoneIds.CenterTopRow, ctbp, "First");
            SetDockProp(DockZoneIds.CenterBottomRow, ctbp, "Second");
        }

        void SetDockProp(string id, JsonObject obj, string key)
        {
            if (obj[key] is JsonValue v && v.TryGetValue(out double d) &&
                Factory.FindById(id) is { } dock)
            {
                dock.Proportion = d;
            }
        }
    }

    private void MigrateLegacyLayout(JsonObject json)
    {
        _logger.LogInformation("Migrating legacy dock layout ({SceneId})", _sceneId);
        foreach (var zoneId in DockZoneIds.AllToolZones)
        {
            var placement = DockZoneIds.ToPlacement(zoneId)!.Value;
            if (!json.TryGetPropertyValue(placement.ToString(), out var zoneNode) ||
                zoneNode is not JsonObject zoneObj ||
                !zoneObj.TryGetPropertyValue("Items", out var itemsNode) ||
                itemsNode is not JsonArray items)
                continue;

            int? selectedIndex = null;
            if (zoneObj.TryGetPropertyValueAsJsonValue("SelectedIndex", out int si))
                selectedIndex = si;

            int i = 0;
            foreach (var node in items)
            {
                if (node is not JsonObject itemObj) { i++; continue; }
                if (!itemObj.TryGetDiscriminator(out Type? extType)) { i++; continue; }
                var extension = ExtensionProvider.Current.AllExtensions
                    .FirstOrDefault(x => x.GetType() == extType) as ToolTabExtension;
                if (extension is null) { i++; continue; }
                if (!extension.TryCreateContext(_editViewModel, out IToolContext? ctx)) { i++; continue; }

                try { ctx.ReadFromJson(itemObj); } catch { /* ignored */ }
                ctx.Placement.Value = placement;
                var dockable = Factory.AddTool(ctx, activate: selectedIndex == i);
                if (dockable is not null && selectedIndex == i)
                {
                    ctx.IsSelected.Value = true;
                }
                i++;
            }
        }
    }

    private void RestoreTools(JsonArray toolsArray)
    {
        foreach (var node in toolsArray)
        {
            if (node is not JsonObject itemObj) continue;
            if (!itemObj.TryGetDiscriminator(out Type? extType)) continue;
            var extension = ExtensionProvider.Current.AllExtensions
                .FirstOrDefault(x => x.GetType() == extType) as ToolTabExtension;
            if (extension is null) continue;
            if (!extension.TryCreateContext(_editViewModel, out IToolContext? ctx)) continue;

            try { ctx.ReadFromJson(itemObj); } catch { /* ignored */ }

            if (itemObj.TryGetPropertyValueAsJsonValue("_zone", out string? zoneId) &&
                DockZoneIds.ToPlacement(zoneId) is { } p)
            {
                ctx.Placement.Value = p;
            }

            var active = false;
            if (itemObj["_isActive"] is JsonValue activeValue)
                activeValue.TryGetValue(out active);

            Factory.AddTool(ctx, activate: active);
            if (active) ctx.IsSelected.Value = true;
        }
    }
}
