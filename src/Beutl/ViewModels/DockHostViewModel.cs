using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Nito.Disposables;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ListAndPlacement =
    (Reactive.Bindings.ReactiveCollection<Beutl.ViewModels.ToolTabViewModel> List,
    Beutl.Extensibility.ToolTabExtension.TabPlacement Placement);

namespace Beutl.ViewModels;

public class DockHostViewModel : IDisposable, IJsonSerializable
{
    private readonly string _sceneId;
    private readonly EditViewModel _editViewModel;
    private readonly ILogger _logger = Log.CreateLogger<DockHostViewModel>();
    private readonly CompositeDisposable _disposables = [];

    public DockHostViewModel(string sceneId, EditViewModel editViewModel)
    {
        _sceneId = sceneId;
        _editViewModel = editViewModel;

        void ConfigureToolsList(ReactiveCollection<ToolTabViewModel> list,
            ReactiveProperty<ToolTabViewModel?> selected)
        {
            var disposables = new List<(ToolTabViewModel, IDisposable)>();

            selected.Subscribe(x =>
                    list.ToObservable()
                        .Where(y => y != x && y.Context.DisplayMode.Value == ToolTabExtension.TabDisplayMode.Docked)
                        .Subscribe(y => y.Context.IsSelected.Value = false))
                .DisposeWith(_disposables);

            list.ObserveAddChanged()
                .Subscribe(x =>
                {
                    var disposable = x.Context.IsSelected.Subscribe(w =>
                    {
                        if (w && x.Context.DisplayMode.Value == ToolTabExtension.TabDisplayMode.Docked)
                        {
                            selected.Value = x;
                        }
                        else
                        {
                            selected.Value = list.FirstOrDefault(xx =>
                                xx.Context.IsSelected.Value && xx.Context.DisplayMode.Value ==
                                ToolTabExtension.TabDisplayMode.Docked);
                        }
                    });
                    disposables.Add((x, disposable));
                })
                .DisposeWith(_disposables);

            list.ObserveRemoveChanged()
                .Subscribe(i =>
                {
                    int index = disposables.FindIndex(x => x.Item1 == i);
                    if (0 > index) return;

                    disposables[index].Item2.Dispose();
                    disposables.RemoveAt(index);
                })
                .DisposeWith(_disposables);

            _disposables.Add(new AnonymousDisposable(() =>
                disposables.ForEach(x => x.Item2.Dispose())));
        }

        ConfigureToolsList(LeftUpperTopTools, SelectedLeftUpperTopTool);
        ConfigureToolsList(LeftUpperBottomTools, SelectedLeftUpperBottomTool);
        ConfigureToolsList(LeftLowerTopTools, SelectedLeftLowerTopTool);
        ConfigureToolsList(LeftLowerBottomTools, SelectedLeftLowerBottomTool);
        ConfigureToolsList(RightUpperTopTools, SelectedRightUpperTopTool);
        ConfigureToolsList(RightUpperBottomTools, SelectedRightUpperBottomTool);
        ConfigureToolsList(RightLowerTopTools, SelectedRightLowerTopTool);
        ConfigureToolsList(RightLowerBottomTools, SelectedRightLowerBottomTool);
    }

    public ReactiveCollection<ToolTabViewModel> LeftUpperTopTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftUpperTopTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> LeftUpperBottomTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftUpperBottomTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> LeftLowerTopTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftLowerTopTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> LeftLowerBottomTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftLowerBottomTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightUpperTopTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightUpperTopTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightUpperBottomTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightUpperBottomTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightLowerTopTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightLowerTopTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightLowerBottomTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightLowerBottomTool { get; } = new();

    public ReDockSizeProportionViewModel LeftRightProportion { get; } = new();

    public ReDockSizeProportionViewModel TopLeftRightProportion { get; } = new();

    public SplittedViewSizeProportionViewModel LeftTopBottomProportion { get; } = new();

    public SplittedViewSizeProportionViewModel CenterTopBottomProportion { get; } = new();

    public SplittedViewSizeProportionViewModel RightTopBottomProportion { get; } = new();

    public SplittedViewSizeProportionViewModel BottomLeftRightProportion { get; } = new();

    public ReactiveCollection<ToolTabViewModel>[] GetNestedTools()
    {
        return
        [
            LeftUpperTopTools,
            LeftUpperBottomTools,
            LeftLowerTopTools,
            LeftLowerBottomTools,
            RightUpperTopTools,
            RightUpperBottomTools,
            RightLowerTopTools,
            RightLowerBottomTools
        ];
    }

    public ListAndPlacement[] GetNestedToolsWithPlacement()
    {
        return
        [
            (LeftUpperTopTools, ToolTabExtension.TabPlacement.LeftUpperTop),
            (LeftUpperBottomTools, ToolTabExtension.TabPlacement.LeftUpperBottom),
            (LeftLowerTopTools, ToolTabExtension.TabPlacement.LeftLowerTop),
            (LeftLowerBottomTools, ToolTabExtension.TabPlacement.LeftLowerBottom),
            (RightUpperTopTools, ToolTabExtension.TabPlacement.RightUpperTop),
            (RightUpperBottomTools, ToolTabExtension.TabPlacement.RightUpperBottom),
            (RightLowerTopTools, ToolTabExtension.TabPlacement.RightLowerTop),
            (RightLowerBottomTools, ToolTabExtension.TabPlacement.RightLowerBottom)
        ];
    }

    public IEnumerable<ToolTabViewModel> GetAllTools()
    {
        return GetNestedTools().SelectMany(i => i);
    }

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext
    {
        return GetAllTools()
            .Select(i => i.Context)
            .OfType<T>()
            .FirstOrDefault(condition);
    }

    public T? FindToolTab<T>()
        where T : IToolContext
    {
        return FindToolTab<T>(_ => true);
    }

    public bool OpenToolTab(IToolContext item)
    {
        _logger.LogInformation("'{ToolTabName}' has been opened. ({SceneId})", item.Extension.Name, _sceneId);
        try
        {
            var tools = GetAllTools();
            // ReSharper disable PossibleMultipleEnumeration
            if (tools.Any(x => x.Context == item))
            {
                item.IsSelected.Value = true;
                return true;
            }
            else if (!item.Extension.CanMultiple
                     && tools.Any(x => x.Context.Extension == item.Extension))
            {
                return false;
            }
            else
            {
                var list = GetNestedToolsWithPlacement()
                    .FirstOrDefault(i => i.Placement == item.Placement.Value)
                    .List;
                if (list == null)
                {
                    _logger.LogWarning("Placement is invalid. ({Placement}, {SceneId})", item.Placement.Value,
                        _sceneId);
                    return false;
                }

                item.IsSelected.Value = true;
                list.Add(new ToolTabViewModel(item, _editViewModel));
                return true;
            }
            // ReSharper restore PossibleMultipleEnumeration
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to OpenToolTab.");
            return false;
        }
    }

    public void CloseToolTab(IToolContext item)
    {
        _logger.LogInformation("CloseToolTab {ToolName}", item.Extension.Name);
        try
        {
            foreach (ReactiveCollection<ToolTabViewModel> tools in GetNestedTools())
            {
                if (tools.FirstOrDefault(x => x.Context == item) is { } found)
                {
                    tools.Remove(found);
                    break;
                }
            }

            item.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to CloseToolTab.");
        }
    }

    public void Dispose()
    {
        foreach (var tools in GetNestedTools())
        {
            foreach (ToolTabViewModel item in tools)
            {
                item.Dispose();
            }

            tools.Clear();
        }

        _disposables.Dispose();
    }

    public void WriteToJson(JsonObject json)
    {
        foreach (var (list, placement) in GetNestedToolsWithPlacement())
        {
            var jsonObject = new JsonObject();
            var jsonArray = new JsonArray();
            int selectedIndex = 0;

            foreach (ToolTabViewModel? item in list)
            {
                var itemJson = new JsonObject();
                item.Context.WriteToJson(itemJson);

                itemJson.WriteDiscriminator(item.Context.Extension.GetType());
                jsonArray.Add(itemJson);

                if (item.Context.IsSelected.Value &&
                    item.Context.DisplayMode.Value == ToolTabExtension.TabDisplayMode.Docked)
                {
                    jsonObject["SelectedIndex"] = selectedIndex;
                }
                else
                {
                    selectedIndex++;
                }
            }

            jsonObject["Items"] = jsonArray;
            json[placement.ToString()] = jsonObject;
        }

        json[nameof(LeftRightProportion)] = CreateJson(LeftRightProportion);
        json[nameof(TopLeftRightProportion)] = CreateJson(TopLeftRightProportion);
        json[nameof(LeftTopBottomProportion)] = CreateJson(LeftTopBottomProportion);
        json[nameof(CenterTopBottomProportion)] = CreateJson(CenterTopBottomProportion);
        json[nameof(RightTopBottomProportion)] = CreateJson(RightTopBottomProportion);
        json[nameof(BottomLeftRightProportion)] = CreateJson(BottomLeftRightProportion);
        return;

        static JsonObject CreateJson(IJsonSerializable serializable)
        {
            var obj = new JsonObject();
            serializable.WriteToJson(obj);
            return obj;
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        foreach (var (list, placement) in GetNestedToolsWithPlacement())
        {
            if (!json.TryGetPropertyValue(placement.ToString(), out JsonNode? node)) continue;
            if (node is not JsonObject tabObject) continue;
            if (!tabObject.TryGetPropertyValue("Items", out JsonNode? itemsNode)) continue;
            if (itemsNode is not JsonArray listItems) continue;

            RestoreTabItems(listItems, list);

            if (tabObject.TryGetPropertyValueAsJsonValue("SelectedIndex", out int index)
                && 0 <= index && index < list.Count)
            {
                list[index].Context.IsSelected.Value = true;
            }
        }

        RestoreProportion(LeftRightProportion, nameof(LeftRightProportion));
        RestoreProportion(TopLeftRightProportion, nameof(TopLeftRightProportion));
        RestoreProportion(LeftTopBottomProportion, nameof(LeftTopBottomProportion));
        RestoreProportion(CenterTopBottomProportion, nameof(CenterTopBottomProportion));
        RestoreProportion(RightTopBottomProportion, nameof(RightTopBottomProportion));
        RestoreProportion(BottomLeftRightProportion, nameof(BottomLeftRightProportion));


        // 何もタブを開いていない場合、デフォルトのタブを開く
        if (!GetAllTools().Any())
        {
            OpenDefaultTabs();
        }

        return;

        void RestoreTabItems(JsonArray source, ReactiveCollection<ToolTabViewModel> destination)
        {
            destination.Clear();
            foreach (JsonNode? item in source)
            {
                if (item is JsonObject itemObject
                    && itemObject.TryGetDiscriminator(out Type? type)
                    && ExtensionProvider.Current.AllExtensions.FirstOrDefault(x => x.GetType() == type) is
                        ToolTabExtension extension
                    && extension.TryCreateContext(_editViewModel, out IToolContext? context))
                {
                    context.ReadFromJson(itemObject);
                    destination.Add(new ToolTabViewModel(context, _editViewModel));
                }
            }
        }

        // Restore proportions safely
        void RestoreProportion(IJsonSerializable serializable, string name)
        {
            if (json.TryGetPropertyValue(name, out JsonNode? proportionNode)
                && proportionNode is JsonObject proportion)
            {
                serializable.ReadFromJson(proportion);
            }
        }
    }

    public void OpenDefaultTabs()
    {
        var tabs = new ToolTabExtension[]
        {
            TimelineTabExtension.Instance, SourceOperatorsTabExtension.Instance, LibraryTabExtension.Instance
        };
        foreach (var ext in tabs)
        {
            if (ext.TryCreateContext(_editViewModel, out IToolContext? tab))
                OpenToolTab(tab);
        }
    }
}
