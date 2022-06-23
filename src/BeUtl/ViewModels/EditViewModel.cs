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

public sealed class EditViewModel : IEditorContext
{
    private readonly CompositeDisposable _disposables = new();

    public EditViewModel(Scene scene)
    {
        Scene = scene;
        Player = new PlayerViewModel(scene);
        //Easings = new EasingsViewModel();
        //Property = scene.GetObservable(Scene.SelectedItemProperty)
        //    .Select(o => o == null ? null : new PropertiesEditorViewModel(o))
        //    .DisposePreviousValue()
        //    .ToReadOnlyReactivePropertySlim()
        //    .AddTo(_disposables);
        Commands = new KnownCommandsImpl(scene);

        BottomTabItems = new CoreList<IToolContext>() { ResetBehavior = ResetBehavior.Remove };
        RightTabItems = new CoreList<IToolContext>() { ResetBehavior = ResetBehavior.Remove };

        if (TimelineTabExtension.Instance.TryCreateContext(this, out IToolContext? context))
        {
            Timeline = (TimelineViewModel?)context!;
            BottomTabItems.Add(context);
        }
        else
        {
            throw new Exception();
        }

        RestoreState();
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }

    public CoreList<IToolContext> BottomTabItems { get; }

    public CoreList<IToolContext> RightTabItems { get; }

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

        foreach (var item in BottomTabItems.AsSpan())
        {
            item.Dispose();
        }
        foreach (var item in RightTabItems.AsSpan())
        {
            item.Dispose();
        }
    }

    public bool OpenToolTab(IToolContext item)
    {
        if (BottomTabItems.Contains(item) || RightTabItems.Contains(item))
        {
            item.IsSelected.Value = true;
            return true;
        }
        else if (!item.Extension.CanMultiple
            && (BottomTabItems.Any(i => i.Extension == item.Extension)
            || RightTabItems.Any(i => i.Extension == item.Extension)))
        {
            return false;
        }
        else
        {
            (item.Placement == ToolTabExtension.TabPlacement.Bottom ? BottomTabItems : RightTabItems).Add(item);
            return true;
        }
    }

    public void CloseToolTab(IToolContext item)
    {
        if (!BottomTabItems.Remove(item))
        {
            RightTabItems.Remove(item);
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
