using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Editors;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class PathEditorTabViewModel : IDisposable, IPathEditorViewModel, IToolContext
{
    private readonly CompositeDisposable _disposables = [];

    public PathEditorTabViewModel(EditViewModel editViewModel)
    {
        EditViewModel = editViewModel;
        Context = FigureContext.Select(v => v?.ParentContext ?? Observable.Return<GeometryEditorViewModel?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        PathGeometry = Context.Select(v => v?.Value ?? Observable.Return<Geometry?>(null))
            .Switch()
            .Select(v => v as PathGeometry)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        PathFigure = FigureContext.Select(v => v?.Value ?? Observable.Return<PathFigure?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Element = Context.Select(v => v?.GetService<Element>())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        SourceOperator = Context.Select(v => v?.GetService<SourceOperator>() as StyledSourcePublisher)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsClosed = PathFigure.Select(g => g?.GetObservable(Media.PathFigure.IsClosedProperty) ?? Observable.Return(false))
            .Switch()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables);

        FigureContext.Subscribe(_ => SelectedOperation.Value = null)
            .DisposeWith(_disposables);
    }

    public EditViewModel EditViewModel { get; }

    public IReactiveProperty<PathFigureEditorViewModel?> FigureContext { get; } = new ReactiveProperty<PathFigureEditorViewModel?>();

    public ReadOnlyReactivePropertySlim<GeometryEditorViewModel?> Context { get; }

    public IReadOnlyReactiveProperty<PathGeometry?> PathGeometry { get; }

    public IReadOnlyReactiveProperty<PathFigure?> PathFigure { get; }

    public IReadOnlyReactiveProperty<Element?> Element { get; }

    public ReadOnlyReactivePropertySlim<StyledSourcePublisher?> SourceOperator { get; }

    public IReactiveProperty<PathSegment?> SelectedOperation { get; } = new ReactiveProperty<PathSegment?>();

    public ReadOnlyReactiveProperty<bool> IsClosed { get; }

    public IReactiveProperty<bool> Symmetry { get; } = new ReactiveProperty<bool>(true);

    public IReactiveProperty<bool> Asymmetry { get; } = new ReactiveProperty<bool>(false);

    public IReactiveProperty<bool> Separately { get; } = new ReactiveProperty<bool>(false);

    public ToolTabExtension Extension { get; } = PathEditorTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.PathEditor;

    public ToolTabExtension.TabPlacement Placement { get; } = ToolTabExtension.TabPlacement.Bottom;

    // FigureContextがcontext引数と同じ場合、編集を終了
    public void StartOrFinishEdit(PathFigureEditorViewModel context)
    {
        if (FigureContext.Value == context)
        {
            FigureContext.Value = null;
            ListEditorViewModel<PathSegment>? group = context.Group.Value;
            if (group != null)
            {
                foreach (ListItemEditorViewModel<PathSegment> item in group.Items)
                {
                    if (item.Context is PathOperationEditorViewModel opEditor
                        && opEditor.ProgrammaticallyExpanded)
                    {
                        opEditor.IsExpanded.Value = false;
                    }
                }
            }
        }
        else
        {
            // Groupプロパティを初期化
            if (!context.IsExpanded.Value)
            {
                context.IsExpanded.Value = true;
            }

            FigureContext.Value = context;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        FigureContext.Dispose();
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
