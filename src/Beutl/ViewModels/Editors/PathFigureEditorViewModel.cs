using System.Text.Json.Nodes;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Media;
using Beutl.Operation;
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

                            Properties.Value = new PropertiesEditorViewModel(group,
                                p => p == group.StartPoint
                                     || p == group.IsClosed);
                        }

                        AcceptChild();
                    })
                    .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
            .DisposeWith(Disposables);

        EditingPath = _editViewModel
            .Select(v => v?.Player.PathEditor.PathFigure ?? Observable.ReturnThenNever<PathFigure?>(null))
            .Switch()
            .CombineLatest(Value)
            .Select(t => t.First == t.Second && t.First != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value.Select(i => i.ToAvaGeometrySync(CurrentTime))
            .CombineWithPrevious()
            // Null-conditionalアクセスがグレーアウトしているが必要なはず...
            .Do(t => t.OldValue.Item2?.Dispose())
            .Select(t => t.NewValue.Item1)
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
        var visitor = new Visitor(this);
        Group.Value?.Accept(visitor);

        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void AddItem(Type type)
    {
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
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out var isExpandedNode)
                && isExpandedNode is JsonValue isExpanded)
            {
                IsExpanded.Value = (bool)isExpanded;
            }

            Properties.Value?.ReadFromJson(json);

            if (Group.Value != null
                && json.TryGetPropertyValue(nameof(Group), out var groupNode)
                && groupNode is JsonObject group)
            {
                Group.Value.ReadFromJson(group);
            }
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            Properties.Value?.WriteToJson(json);
            if (Group.Value != null)
            {
                var group = new JsonObject();
                Group.Value.WriteToJson(group);
                json[nameof(Group)] = group;
            }
        }
        catch
        {
        }
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

    public void AddSegment(PathSegment segment, int index)
    {
        Group.Value?.AddItem(segment);
    }

    protected override void Dispose(bool disposing)
    {
        if (_editViewModel.Value is { } editViewModel)
        {
            var tab = editViewModel.FindToolTab<PathEditorTabViewModel>();
            if (tab != null && tab.FigureContext.Value == this)
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

    private sealed record Visitor(PathFigureEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
