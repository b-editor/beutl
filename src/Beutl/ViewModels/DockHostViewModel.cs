using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
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

    public IToolContext? FindToolContext(Type extensionType)
    {
        return Factory.EnumerateTools()
            .Select(t => t.ToolContext)
            .FirstOrDefault(ctx => ctx.Extension.GetType() == extensionType);
    }

    public bool OpenToolTab(IToolContext item)
    {
        return OpenToolTab(item, target: null);
    }

    public bool OpenToolTab(IToolContext item, IToolDock? target)
    {
        return OpenToolTab(item, target, replacing: null);
    }

    private bool OpenToolTab(IToolContext item, IToolDock? target, NewToolTabDockable? replacing)
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

            // Another tab of a tool already open goes into the same dock, e.g. next to a pinned tab.
            target ??= Factory.EnumerateTools()
                .FirstOrDefault(t => t.ToolContext.Extension == item.Extension)?.Owner as IToolDock;

            var dockable = replacing is null
                ? Factory.AddTool(item, target)
                : Factory.ReplaceNewToolTab(replacing, item);
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

        var extensions = _editViewModel.ExtensionProvider.AllExtensions
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

    internal bool OpenToolTabFromExtension(ToolTabExtension ext, IToolDock? target)
    {
        return OpenToolTabFromExtension(ext, target, replacing: null);
    }

    /// <summary>Opens <paramref name="ext"/> in place of an empty tab the add button opened.</summary>
    internal bool ReplaceNewToolTab(NewToolTabDockable newTab, ToolTabExtension ext)
    {
        return OpenToolTabFromExtension(ext, target: null, replacing: newTab);
    }

    private bool OpenToolTabFromExtension(ToolTabExtension ext, IToolDock? target, NewToolTabDockable? replacing)
    {
        if (!ext.TryCreateContext(_editViewModel, out IToolContext? tab)
            || tab is null)
        {
            return false;
        }

        if (OpenToolTab(tab, target, replacing))
        {
            return true;
        }

        tab.Dispose();
        return false;
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
        json["DockLayout"] = DockLayoutJsonWriter.SaveNode(Layout.Value);
    }

    public void ReadFromJson(JsonObject json)
    {
        _logger.LogInformation("Reading DockHostViewModel from JSON ({SceneId})", _sceneId);

        if (IsCurrentVersion(json) &&
            json.TryGetPropertyValue("DockLayout", out var layoutNode) &&
            layoutNode is JsonObject layoutObj)
        {
            try
            {
                var reader = new DockLayoutJsonReader(
                    Factory, _editViewModel, _logger, _sceneId, restoredTools: null, restoringArrangementOnly: false);
                var restored = reader.RestoreNode(layoutObj);
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

        if (!HasOpenTabs())
        {
            OpenDefaultTabs();
        }
    }

    public void ResetLayout()
    {
        ResetToDefaultLayout("user requested");
        OpenDefaultTabs();
        _editViewModel.OpenSelectionInPropertyTabs();
    }

    /// <summary>
    /// Captures the current layout in the same shape <see cref="WriteToJson"/> writes, for
    /// <see cref="ApplyLayout"/> to restore later.
    /// </summary>
    /// <remarks>
    /// Per-tool state (selected element ids, search text, ...) is dropped; it belongs to the scene
    /// it was captured from.
    /// </remarks>
    public JsonObject CaptureLayout()
    {
        EnsureDefaultLayout();
        return new JsonObject
        {
            ["_dockVersion"] = DockVersion,
            ["DockLayout"] = DockLayoutJsonWriter.SaveNode(Layout.Value, includeToolState: false),
        };
    }

    /// <summary>
    /// Replaces the current layout with a previously captured one.
    /// </summary>
    /// <remarks>
    /// The outgoing tool contexts are disposed and the incoming layout builds fresh ones against
    /// this editor, so a layout captured elsewhere restores the arrangement only.
    /// </remarks>
    public bool ApplyLayout(JsonObject layout)
    {
        _logger.LogInformation("Applying a saved dock layout ({SceneId})", _sceneId);

        // Restore first: a malformed preset must not leave the editor without a layout.
        IRootDock restored;
        // Collects every tool built during the walk, so a mid-walk failure can dispose them.
        var built = new List<BeutlToolDockable>();
        // A saved layout carries no tool state (see CaptureLayout), so the readers must be skipped.
        var reader = new DockLayoutJsonReader(
            Factory, _editViewModel, _logger, _sceneId, built, restoringArrangementOnly: true);
        try
        {
            if (!IsCurrentVersion(layout))
            {
                _logger.LogWarning(
                    "Saved dock layout was written by an incompatible version ({SceneId})", _sceneId);
                return false;
            }

            if (!layout.TryGetPropertyValue("DockLayout", out JsonNode? layoutNode)
                || layoutNode is not JsonObject layoutObj)
            {
                _logger.LogWarning("Saved dock layout has no DockLayout node ({SceneId})", _sceneId);
                return false;
            }

            if (reader.RestoreNode(layoutObj) is not IRootDock rootDock)
            {
                _logger.LogWarning("Saved dock layout did not restore to an IRootDock ({SceneId})", _sceneId);
                DisposeAll(built, "partially restored");
                return false;
            }

            restored = rootDock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore a saved dock layout ({SceneId})", _sceneId);
            DisposeAll(built, "partially restored");
            return false;
        }

        // Dispose after the swap, and directly — CloseDockable would walk the old tree.
        var previousTools = Factory.EnumerateTools().ToList();

        Factory.SetRootDock(restored);
        Factory.InitLayout(restored);
        Layout.Value = restored;
        _layoutInitialized = true;

        DisposeAll(previousTools, "replaced");

        if (!HasOpenTabs())
        {
            OpenDefaultTabs();
        }

        _editViewModel.OpenSelectionInPropertyTabs();
        return true;
    }

    // An empty tab left open is part of the layout too, so restoring it must not add the default tools.
    private bool HasOpenTabs()
    {
        return Factory.EnumerateTools().Any()
               || BeutlDockFactory.Traverse(Layout.Value).OfType<NewToolTabDockable>().Any();
    }

    private void DisposeAll(IEnumerable<BeutlToolDockable> tools, string what)
    {
        foreach (BeutlToolDockable tool in tools)
        {
            try
            {
                tool.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose a {What} tool tab ({SceneId})", what, _sceneId);
            }
        }
    }

    private static bool IsCurrentVersion(JsonObject json)
    {
        return json.TryGetPropertyValue("_dockVersion", out JsonNode? node)
               && node is JsonValue value
               && value.TryGetValue(out int version)
               && version == DockVersion;
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
}
