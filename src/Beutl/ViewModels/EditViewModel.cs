using System.Numerics;
using System.Text.Json.Nodes;

using Beutl.Api.Services;

using Beutl.Framework;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ToolTabViewModel : IDisposable
{
    public ToolTabViewModel(IToolContext context)
    {
        Context = context;
    }

    public IToolContext Context { get; }

    public int Order { get; set; } = -1;

    public void Dispose()
    {
        Context.Dispose();
    }
}

public sealed class EditViewModel : IEditorContext, ITimelineOptionsProvider
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ExtensionProvider _extensionProvider = ServiceLocator.Current.GetRequiredService<ExtensionProvider>();

    public EditViewModel(Scene scene)
    {
        Scene = scene;
        Player = new PlayerViewModel(scene, IsEnabled);
        Commands = new KnownCommandsImpl(scene);
        SelectedObject = new ReactiveProperty<CoreObject?>()
            .DisposeWith(_disposables);

        BottomTabItems = new CoreList<ToolTabViewModel>() { ResetBehavior = ResetBehavior.Remove };
        RightTabItems = new CoreList<ToolTabViewModel>() { ResetBehavior = ResetBehavior.Remove };

        Scale = Options.Select(o => o.Scale);
        Offset = Options.Select(o => o.Offset);

        RestoreState();
    }

    public Scene Scene { get; set; }

    public CoreList<ToolTabViewModel> BottomTabItems { get; }

    public CoreList<ToolTabViewModel> RightTabItems { get; }

    public ReactiveProperty<CoreObject?> SelectedObject { get; }

    public ReactivePropertySlim<bool> IsEnabled { get; } = new(true);

    public PlayerViewModel Player { get; }

    //public EasingsViewModel Easings { get; }

    //public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Property { get; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public string EdittingFile => Scene.FileName;

    public IKnownEditorCommands? Commands { get; }

    public IReactiveProperty<TimelineOptions> Options { get; } = new ReactiveProperty<TimelineOptions>(new TimelineOptions());

    public IObservable<float> Scale { get; }

    public IObservable<Vector2> Offset { get; }

    IReactiveProperty<bool> IEditorContext.IsEnabled => IsEnabled;

    public void Dispose()
    {
        SaveState();
        _disposables.Dispose();
        Player.Dispose();

        foreach (ToolTabViewModel item in BottomTabItems.GetMarshal().Value)
        {
            item.Dispose();
        }
        foreach (ToolTabViewModel item in RightTabItems.GetMarshal().Value)
        {
            item.Dispose();
        }
    }

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext
    {
        for (int i = 0; i < BottomTabItems.Count; i++)
        {
            var item = BottomTabItems[i];
            if (item.Context is T typed && condition(typed))
            {
                return typed;
            }
        }

        for (int i = 0; i < RightTabItems.Count; i++)
        {
            var item = RightTabItems[i];
            if (item.Context is T typed && condition(typed))
            {
                return typed;
            }
        }

        return default;
    }

    public T? FindToolTab<T>()
        where T : IToolContext
    {
        for (int i = 0; i < BottomTabItems.Count; i++)
        {
            if (BottomTabItems[i].Context is T typed)
            {
                return typed;
            }
        }

        for (int i = 0; i < RightTabItems.Count; i++)
        {
            if (RightTabItems[i].Context is T typed)
            {
                return typed;
            }
        }

        return default;
    }

    public bool OpenToolTab(IToolContext item)
    {
        if (BottomTabItems.Any(x => x.Context == item) || RightTabItems.Any(x => x.Context == item))
        {
            item.IsSelected.Value = true;
            return true;
        }
        else if (!item.Extension.CanMultiple
            && (BottomTabItems.Any(x => x.Context.Extension == item.Extension)
            || RightTabItems.Any(x => x.Context.Extension == item.Extension)))
        {
            return false;
        }
        else
        {
            CoreList<ToolTabViewModel> list = item.Placement == ToolTabExtension.TabPlacement.Bottom ? BottomTabItems : RightTabItems;
            item.IsSelected.Value = true;
            list.Add(new ToolTabViewModel(item));
            return true;
        }
    }

    public void CloseToolTab(IToolContext item)
    {
        if (BottomTabItems.FirstOrDefault(x => x.Context == item) is { } found0)
        {
            BottomTabItems.Remove(found0);
        }
        else if (RightTabItems.FirstOrDefault(x => x.Context == item) is { } found1)
        {
            RightTabItems.Remove(found1);
        }

        item.Dispose();
    }

    private string ViewStateDirectory()
    {
        string directory = Path.GetDirectoryName(EdittingFile)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState()
    {
        string viewStateDir = ViewStateDirectory();
        var json = new JsonObject
        {
            ["selected-layer"] = (SelectedObject.Value as Layer)?.ZIndex ?? -1,
            ["max-layer-count"] = Options.Value.MaxLayerCount,
            ["scale"] = Options.Value.Scale,
            ["offset"] = new JsonObject
            {
                ["x"] = Options.Value.Offset.X,
                ["y"] = Options.Value.Offset.Y,
            }
        };

        var bottomItems = new JsonArray();
        foreach (ToolTabViewModel? item in BottomTabItems.OrderBy(x => x.Order))
        {
            JsonNode itemJson = new JsonObject();
            item.Context.WriteToJson(ref itemJson);

            itemJson["@type"] = TypeFormat.ToString(item.Context.Extension.GetType());
            bottomItems.Add(itemJson);
        }

        json["bottom-items"] = bottomItems;

        var rightItems = new JsonArray();
        foreach (ToolTabViewModel? item in RightTabItems.OrderBy(x => x.Order))
        {
            JsonNode itemJson = new JsonObject();
            item.Context.WriteToJson(ref itemJson);

            itemJson["@type"] = TypeFormat.ToString(item.Context.Extension.GetType());
            rightItems.Add(itemJson);
        }

        json["right-items"] = rightItems;

        json.JsonSave(Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(EdittingFile)}.config"));
    }

    private void RestoreState()
    {
        string viewStateDir = ViewStateDirectory();
        string viewStateFile = Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(EdittingFile)}.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is not JsonObject jsonObject)
                return;

            try
            {
                int layer = (int?)json["selected-layer"] ?? -1;
                if (layer >= 0)
                {
                    foreach (Layer item in Scene.Children.GetMarshal().Value)
                    {
                        if (item.ZIndex == layer)
                        {
                            SelectedObject.Value = item;
                            break;
                        }
                    }
                }
            }
            catch { }

            var timelineOptions = new TimelineOptions();

            if (jsonObject.TryGetPropertyValue("max-layer-count", out JsonNode? maxLayer)
                && maxLayer is JsonValue maxLayerValue
                && maxLayerValue.TryGetValue(out int maxLayerCount))
            {
                timelineOptions = timelineOptions with
                {
                    MaxLayerCount = maxLayerCount
                };
            }

            if (jsonObject.TryGetPropertyValue("scale", out JsonNode? scaleNode)
                && scaleNode is JsonValue scaleValue
                && scaleValue.TryGetValue(out float scale))
            {
                timelineOptions = timelineOptions with
                {
                    Scale = scale
                };
            }

            if (jsonObject.TryGetPropertyValue("offset", out JsonNode? offsetNode)
                && offsetNode is JsonObject offsetObj
                && offsetObj.TryGetPropertyValue("x", out JsonNode? xNode)
                && offsetObj.TryGetPropertyValue("y", out JsonNode? yNode)
                && xNode is JsonValue xValue
                && yNode is JsonValue yValue
                && xValue.TryGetValue(out float x)
                && yValue.TryGetValue(out float y))
            {
                timelineOptions = timelineOptions with
                {
                    Offset = new Vector2(x, y)
                };
            }

            Options.Value = timelineOptions;

            BottomTabItems.Clear();
            RightTabItems.Clear();

            void RestoreTabItems(JsonArray source, CoreList<ToolTabViewModel> destination)
            {
                int count = 0;
                foreach (JsonNode? item in source)
                {
                    if (item is JsonObject itemObject
                        && itemObject.TryGetPropertyValue("@type", out JsonNode? typeNode)
                        && typeNode is JsonValue typeValue
                        && typeValue.TryGetValue(out string? typeStr)
                        && typeStr != null
                        && TypeFormat.ToType(typeStr) is Type type
                        && _extensionProvider.AllExtensions.FirstOrDefault(x => x.GetType() == type) is ToolTabExtension extension
                        && extension.TryCreateContext(this, out IToolContext? context))
                    {
                        context.ReadFromJson(item);
                        destination.Add(new ToolTabViewModel(context)
                        {
                            Order = count
                        });
                        count++;
                    }
                }
            }

            if (jsonObject.TryGetPropertyValue("bottom-items", out JsonNode? bottomNode)
                && bottomNode is JsonArray bottomItems)
            {
                RestoreTabItems(bottomItems, BottomTabItems);
            }

            if (jsonObject.TryGetPropertyValue("right-items", out JsonNode? rightNode)
                && rightNode is JsonArray rightItems)
            {
                RestoreTabItems(rightItems, RightTabItems);
            }
        }
        else
        {
            if (TimelineTabExtension.Instance.TryCreateContext(this, out IToolContext? tab1))
            {
                OpenToolTab(tab1);
            }

            if (StreamOperatorsTabExtension.Instance.TryCreateContext(this, out IToolContext? tab2))
            {
                OpenToolTab(tab2);
            }

            if (EasingsTabExtension.Instance.TryCreateContext(this, out IToolContext? tab3))
            {
                OpenToolTab(tab3);
            }
        }
    }

    private sealed class KnownCommandsImpl : IKnownEditorCommands
    {
        private readonly Scene _scene;

        public KnownCommandsImpl(Scene scene)
        {
            _scene = scene;
        }

        public ValueTask<bool> OnSave()
        {
            _scene.Save(_scene.FileName);
            foreach (Layer layer in _scene.Children)
            {
                layer.Save(layer.FileName);
            }

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnUndo()
        {
            CommandRecorder.Default.Undo();

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnRedo()
        {
            CommandRecorder.Default.Redo();

            return ValueTask.FromResult(true);
        }
    }
}
