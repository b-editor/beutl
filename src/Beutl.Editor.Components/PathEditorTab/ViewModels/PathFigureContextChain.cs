using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PathEditorTab.ViewModels;

// The path tab and the preview overlay follow the edited figure through the same properties.
internal sealed record PathFigureContextChain(
    ReadOnlyReactivePropertySlim<IGeometryEditorContext?> Context,
    ReadOnlyReactivePropertySlim<Geometry?> Geometry,
    ReadOnlyReactivePropertySlim<PathGeometry?> PathGeometry,
    ReadOnlyReactivePropertySlim<PathFigure?> PathFigure,
    ReadOnlyReactivePropertySlim<Element?> Element)
{
    public static PathFigureContextChain Create(
        IReactiveProperty<IPathFigureEditorContext?> figureContext, CompositeDisposable disposables)
    {
        var context = figureContext.Select(v => v?.GetParentContext() ?? null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        var geometry = context.Select(v => v?.Value ?? Observable.ReturnThenNever<Geometry?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        var pathGeometry = geometry
            .Select(v => v as PathGeometry)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);
        var pathFigure = figureContext.Select(v => v?.Value ?? Observable.ReturnThenNever<PathFigure?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);
        var element = context.Select(v => v?.GetService<Element>())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        return new PathFigureContextChain(context, geometry, pathGeometry, pathFigure, element);
    }

    // Choosing the figure that is already being edited ends its edit. It collapses before it is let go,
    // since letting go can dispose an editor the path tab owns.
    public static void ToggleEditing(
        IReactiveProperty<IPathFigureEditorContext?> figureContext, IPathFigureEditorContext context)
    {
        if (figureContext.Value == context)
        {
            context.CollapseEditedOperations();
            figureContext.Value = null;
        }
        else
        {
            context.ExpandForEditing();
            figureContext.Value = context;
        }
    }
}
