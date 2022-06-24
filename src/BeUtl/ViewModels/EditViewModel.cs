using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Media;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Models;
using BeUtl.ProjectSystem;
using BeUtl.Services.PrimitiveImpls;
using BeUtl.ViewModels.Editors;

using OpenCvSharp;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

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

public sealed class EditViewModel : IEditorContext
{
    private readonly CompositeDisposable _disposables = new();

    public EditViewModel(Scene scene)
    {
        Scene = scene;
        Player = new PlayerViewModel(scene);
        Commands = new KnownCommandsImpl(scene);
        SelectedObject = scene.GetObservable(Scene.SelectedItemProperty)
            .ToReactiveProperty<CoreObject?>()
            .DisposeWith(_disposables);

        BottomTabItems = new CoreList<ToolTabViewModel>() { ResetBehavior = ResetBehavior.Remove };
        RightTabItems = new CoreList<ToolTabViewModel>() { ResetBehavior = ResetBehavior.Remove };

        if (TimelineTabExtension.Instance.TryCreateContext(this, out IToolContext? timeline)
            && OperationsTabExtension.Instance.TryCreateContext(this, out IToolContext? operations))
        {
            Timeline = (TimelineViewModel?)timeline!;
            BottomTabItems.Add(new(timeline));

            Operations = (OperationsEditorViewModel?)operations!;
            RightTabItems.Add(new(operations));
        }
        else
        {
            throw new Exception();
        }

        RestoreState();
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }

    public OperationsEditorViewModel Operations { get; }

    public CoreList<ToolTabViewModel> BottomTabItems { get; }

    public CoreList<ToolTabViewModel> RightTabItems { get; }

    public ReactiveProperty<CoreObject?> SelectedObject { get; }

    public PlayerViewModel Player { get; }

    //public EasingsViewModel Easings { get; }

    //public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Property { get; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public string EdittingFile => Scene.FileName;

    public IKnownEditorCommands? Commands { get; }

    public AnimationTimelineViewModel? RequestingAnimationTimeline { get; internal set; }

    public void Dispose()
    {
        SaveState();
        _disposables.Dispose();

        foreach (ToolTabViewModel item in BottomTabItems.AsSpan())
        {
            item.Dispose();
        }
        foreach (ToolTabViewModel item in RightTabItems.AsSpan())
        {
            item.Dispose();
        }
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
            //["selected-layer"] = Property.Value?.Layer?.ZIndex ?? -1,
            ["max-layer-count"] = Timeline.Options.Value.MaxLayerCount,
            ["scale"] = Timeline.Options.Value.Scale,
            ["offset"] = new JsonObject
            {
                ["x"] = Timeline.Options.Value.Offset.X,
                ["y"] = Timeline.Options.Value.Offset.Y,
            }
        };

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
            if (json == null)
                return;
            var timelineOptions = new TimelineOptions();

            //try
            //{
            //    int layer = (int?)json["selected-layer"] ?? -1;
            //    if (layer >= 0)
            //    {
            //        foreach (Layer item in Scene.Children.AsSpan())
            //        {
            //            if (item.ZIndex == layer)
            //            {
            //                Scene.SelectedItem = item;
            //                break;
            //            }
            //        }
            //    }
            //}
            //catch { }

            try
            {
                int layerCount = (int?)json["max-layer-count"] ?? 100;
                timelineOptions = timelineOptions with
                {
                    MaxLayerCount = layerCount
                };
            }
            catch { }

            try
            {
                float scale = (float?)json["scale"] ?? 1;
                timelineOptions = timelineOptions with
                {
                    Scale = scale
                };
            }
            catch { }

            try
            {
                JsonNode? offset = json["offset"];

                if (offset != null)
                {
                    float x = (float?)offset["x"] ?? 0;
                    float y = (float?)offset["y"] ?? 0;

                    timelineOptions = timelineOptions with
                    {
                        Offset = new System.Numerics.Vector2(x, y)
                    };
                }
            }
            catch { }

            Timeline.Options.Value = timelineOptions;
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
