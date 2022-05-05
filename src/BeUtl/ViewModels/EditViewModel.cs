using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Media;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.Services;
using BeUtl.ViewModels.Editors;

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
        _disposables.Dispose();
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
