using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Api.Services;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed class AiVideoEditingViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer
{
    private readonly Func<AiSourceVideoMode, AiVideoGenerationDialogViewModel> _create;
    private readonly Dictionary<AiSourceVideoMode, AiVideoGenerationDialogViewModel> _pages = [];
    private readonly CompositeDisposable _disposables = [];
    private Task? _disposal;

    public AiVideoEditingViewModel(Func<AiSourceVideoMode, AiVideoGenerationDialogViewModel> create)
    {
        _create = create;
        SelectedTask = new(Tasks[0]);
        SelectedTask.Subscribe(task =>
        {
            if (_disposal is not null) return;
            var previous = ActiveContent.Value;
            bool cached = _pages.TryGetValue(task.Mode, out var page);
            if (!cached) _pages.Add(task.Mode, page = _create(task.Mode));
            if (previous is not null && previous != page) page!.CopySourceIntent(previous);
            ActiveContent.Value = page;
            if (cached && previous != page)
            {
                page!.RefreshModels();
                page.RefreshAvailability();
            }
        }).DisposeWith(_disposables);
        CanChooseTask = ActiveContent.Select(page => page?.IsGenerating.Select(value => !value) ?? Observable.Return(false))
            .Switch().ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
    }

    public IReadOnlyList<AiVideoEditTask> Tasks { get; } =
    [new(AiSourceVideoMode.Edit, Strings.AiVideoEditing), new(AiSourceVideoMode.Extend, Strings.AiVideoExtend), new(AiSourceVideoMode.Motion, Strings.AiVideoMotion)];
    public ReactivePropertySlim<AiVideoEditTask> SelectedTask { get; }
    public ReactivePropertySlim<AiVideoGenerationDialogViewModel?> ActiveContent { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> CanChooseTask { get; }
    public void RefreshModels() => ActiveContent.Value?.RefreshModels();
    public void Dispose() => _ = DisposeAsync();
    public ValueTask DisposeAsync()
    {
        if (_disposal is not null) return new(_disposal);
        _disposables.Dispose(); SelectedTask.Dispose(); ActiveContent.Dispose();
        _disposal = Task.WhenAll(_pages.Values.Select(page => page.DisposeAsync().AsTask()));
        return new(_disposal);
    }
}
internal sealed record AiVideoEditTask(AiSourceVideoMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}
