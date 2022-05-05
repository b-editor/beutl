using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Avalonia.Media;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.Services;
using BeUtl.ViewModels.Editors;

using OpenCvSharp;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public sealed class ExtendedEditTabViewModel : IDisposable
{
    public ExtendedEditTabViewModel(SceneEditorTabExtension extension)
    {
        Extension = extension;
    }

    public SceneEditorTabExtension Extension { get; }

    public ReactivePropertySlim<bool> IsSelected { get; } = new();

    public void Dispose()
    {
    }
}

public sealed class EditViewModel : IEditorContext
{
    private readonly CompositeDisposable _disposables = new();

    public EditViewModel(Scene scene)
    {
        Scene = scene;
        AnimationTimelines = new();
        UsingExtensions = new();
        Player = new PlayerViewModel(scene);
        Timeline = new TimelineViewModel(scene, Player).AddTo(_disposables);
        Easings = new EasingsViewModel();
        Property = scene.GetObservable(Scene.SelectedItemProperty)
            .Select(o => o == null ? null : new PropertiesEditorViewModel(o))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        Commands = new KnownCommandsImpl(scene);

        RestoreState();
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }

    public CoreList<AnimationTimelineViewModel> AnimationTimelines { get; }

    public CoreList<ExtendedEditTabViewModel> UsingExtensions { get; }

    public PlayerViewModel Player { get; }

    public EasingsViewModel Easings { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Property { get; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public string EdittingFile => Scene.FileName;

    public IKnownEditorCommands? Commands { get; }

    public void Dispose()
    {
        SaveState();
        _disposables.Dispose();
        Property.Value?.Dispose();
        foreach (AnimationTimelineViewModel item in AnimationTimelines.AsSpan())
        {
            item.Dispose();
        }
        foreach (ExtendedEditTabViewModel item in UsingExtensions)
        {
            item.Dispose();
        }
    }

    private string ViewStateDirectory()
    {
        string directory = Path.GetDirectoryName(EdittingFile)!;
        // Todo: 後で変更
        directory = Path.Combine(directory, ".beutl", "view-state");
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
            ["selected-layer"] = Property.Value?.Layer?.ZIndex ?? -1,
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

            try
            {
                int layer = (int?)json["selected-layer"] ?? -1;
                if (layer >= 0)
                {
                    foreach (Layer item in Scene.Children.AsSpan())
                    {
                        if (item.ZIndex == layer)
                        {
                            Scene.SelectedItem = item;
                            break;
                        }
                    }
                }
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
    }
}
