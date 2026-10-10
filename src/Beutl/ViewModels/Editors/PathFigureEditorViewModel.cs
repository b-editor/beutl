using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class PathFigureEditorViewModel : ValueEditorViewModel<PathFigure>, IPathFigureEditorContext
{
    private readonly ReactivePropertySlim<EditViewModel?> _editViewModel = new();

    public PathFigureEditorViewModel(IPropertyAdapter<PathFigure> property)
        : base(property)
    {
        _editViewModel.DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                    {
                        Properties.Value?.Dispose();
                        Properties.Value = null;
                        Group.Value?.Dispose();
                        Group.Value = null;

                        if (v is { } group)
                        {
                            var prop = new EnginePropertyAdapter<ICoreList<PathSegment>>(group.Segments, group);
                            Group.Value = new ListEditorViewModel<PathSegment>(prop) { IsExpanded = { Value = true } };

                            Properties.Value = new PropertiesEditorViewModel(group, GetExtensionProvider(),
                                p => p == group.StartPoint
                                     || p == group.IsClosed);
                        }

                        AcceptChild();
                    })
                    .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        EditingPath = _editViewModel
            .Select(v => v?.Player.PathEditor.PathFigure ?? Observable.ReturnThenNever<PathFigure?>(null))
            .Switch()
            .CombineLatest(Value)
            .Select(t => t.First == t.Second && t.First != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value.Select(figure => Observable.Create<Avalonia.Media.Geometry?>(observer =>
            {
                if (figure == null)
                {
                    observer.OnNext(null);
                    return Disposable.Empty;
                }

                var (geometry, subscription) = figure.ToAvaGeometrySync(CurrentTime);
                return new CompositeDisposable(subscription, geometry.Subscribe(observer));
            }))
            .Switch()
            .Subscribe(geometry => PreviewPath.Value = geometry)
            .DisposeWith(Disposables);
    }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<PathSegment>?> Group { get; } = new();

    public ReactivePropertySlim<GeometryEditorViewModel?> ParentContext { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> EditingPath { get; }

    public ReactivePropertySlim<Avalonia.Media.Geometry?> PreviewPath { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
        if (visitor is IServiceProvider serviceProvider)
        {
            ParentContext.Value = serviceProvider.GetService<GeometryEditorViewModel>();
            _editViewModel.Value = serviceProvider.GetService<EditViewModel>();
        }
    }

    private void AcceptChild()
    {
        NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), Group.Value, Properties.Value);
    }

    public void AddItem(Type type)
    {
        if (!IsElementEditable) return;
        if (Value.Value is { } group
            && Activator.CreateInstance(type) is PathSegment instance)
        {
            group.Segments.Add(instance);
            Commit();
        }
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        NestedEditorContextHelper.ReadNestedJson(json, IsExpanded, Properties.Value, Group.Value);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        NestedEditorContextHelper.WriteNestedJson(json, IsExpanded.Value, Properties.Value, Group.Value);
    }

    public IGeometryEditorContext? GetParentContext() => ParentContext.Value as IGeometryEditorContext;

    public void ExpandForEditing()
    {
        if (!IsExpanded.Value)
        {
            IsExpanded.Value = true;
        }
    }

    public void CollapseEditedOperations()
    {
        if (Group.Value is { } group)
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

    public void ExpandOperationForSegment(PathSegment segment)
    {
        if (Group.Value is { } group)
        {
            foreach (ListItemEditorViewModel<PathSegment> item in group.Items)
            {
                if (item.Context is PathOperationEditorViewModel itemvm)
                {
                    if (ReferenceEquals(itemvm.Value.Value, segment))
                    {
                        itemvm.IsExpanded.Value = true;
                        itemvm.ProgrammaticallyExpanded = true;
                    }
                    else if (itemvm.ProgrammaticallyExpanded)
                    {
                        itemvm.IsExpanded.Value = false;
                    }
                }
            }
        }
    }

    public new void InvalidateFrameCache()
    {
        base.InvalidateFrameCache();
    }

    public int GetSegmentIndex(PathSegment segment)
    {
        return Group.Value?.List.Value?.IndexOf(segment) ?? -1;
    }

    public void RemoveSegment(int index)
    {
        Group.Value?.RemoveItem(index);
    }

    public void AddSegment(PathSegment segment)
    {
        Group.Value?.AddItem(segment);
    }

    protected override void Dispose(bool disposing)
    {
        if (_editViewModel.Value is { } editViewModel)
        {
            if (editViewModel.FindToolTab<PathEditorTabViewModel>(t => t.FigureContext.Value == this) is { } tab)
            {
                tab.FigureContext.Value = null;
            }

            if (editViewModel is { Player.PathEditor: { } pathEditor }
                && pathEditor.FigureContext.Value == this)
            {
                pathEditor.FigureContext.Value = null;
            }
        }

        PreviewPath.Value = null;

        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }

}
