using Beutl.Logging;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

internal sealed class EditorSelectionImpl : IEditorSelection, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<EditorSelectionImpl>();
    private readonly CompositeDisposable _disposables = [];

    public EditorSelectionImpl()
    {
        SelectedObject = new ReactiveProperty<CoreObject?>()
            .DisposeWith(_disposables);

        SelectedObject.CombineWithPrevious()
            .Subscribe(v =>
            {
                if (v.OldValue is IHierarchical oldHierarchical)
                    oldHierarchical.DetachedFromHierarchy -= OnSelectedObjectDetachedFromHierarchy;

                if (v.NewValue is IHierarchical newHierarchical)
                    newHierarchical.DetachedFromHierarchy += OnSelectedObjectDetachedFromHierarchy;
            })
            .DisposeWith(_disposables);

        SelectedLayerNumber = SelectedObject.Select(v =>
                (v as Element)?.GetObservable(Element.ZIndexProperty).Select(i => (int?)i) ??
                Observable.ReturnThenNever<int?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
    }

    public ReactiveProperty<CoreObject?> SelectedObject { get; }

    public ReadOnlyReactivePropertySlim<int?> SelectedLayerNumber { get; }

    IReactiveProperty<CoreObject?> IEditorSelection.SelectedObject => SelectedObject;

    IReadOnlyReactiveProperty<int?> IEditorSelection.SelectedLayerNumber => SelectedLayerNumber;

    private void OnSelectedObjectDetachedFromHierarchy(object? sender, HierarchyAttachmentEventArgs e)
    {
        _logger.LogInformation("Selected object detached from hierarchy, clearing selection.");
        SelectedObject.Value = null;
    }

    public void Dispose()
    {
        if (SelectedObject.Value is IHierarchical hierarchical)
            hierarchical.DetachedFromHierarchy -= OnSelectedObjectDetachedFromHierarchy;

        _disposables.Dispose();
    }
}
