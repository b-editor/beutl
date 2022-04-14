using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Media;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.ProjectSystem;
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

public sealed class EditViewModel : IDisposable
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
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }

    public CoreList<AnimationTimelineViewModel> AnimationTimelines { get; }

    public CoreList<ExtendedEditTabViewModel> UsingExtensions { get; }

    public PlayerViewModel Player { get; }

    public EasingsViewModel Easings { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Property { get; }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _disposables.Dispose();
    }
}
