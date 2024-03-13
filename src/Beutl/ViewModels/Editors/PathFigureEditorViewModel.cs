using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Operation;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Editors;

public sealed class PathFigureEditorViewModel : ValueEditorViewModel<PathFigure>
{
    private readonly ReactivePropertySlim<EditViewModel?> _editViewModel = new();
    private bool _invalidated = true;

    public PathFigureEditorViewModel(IAbstractProperty<PathFigure> property)
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

                    if (v is PathFigure group)
                    {
                        var prop = new CorePropertyImpl<PathSegments>(PathFigure.SegmentsProperty, group);
                        Group.Value = new ListEditorViewModel<PathSegment>(prop)
                        {
                            IsExpanded = { Value = true }
                        };

                        Properties.Value = new PropertiesEditorViewModel(group,
                            (p, m) => p == PathFigure.StartPointProperty
                                   || p == PathFigure.IsClosedProperty);
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .WhereNotNull()
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v))
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Subscribe(t =>
            {
                if (t.OldValue != null)
                    t.OldValue.Invalidated -= OnFigureInvalidated;

                if (t.NewValue != null)
                    t.NewValue.Invalidated += OnFigureInvalidated;
            })
            .DisposeWith(Disposables);

        EditingPath = _editViewModel.Select(v => v?.Player.PathEditor.PathFigure ?? Observable.Return<PathFigure?>(null))
            .Switch()
            .CombineLatest(Value)
            .Select(t => t.First == t.Second && t.First != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        RecreatePreviewPath();
    }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<PathSegment>?> Group { get; } = new();

    public ReactivePropertySlim<GeometryEditorViewModel?> ParentContext { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> EditingPath { get; }

    public ReactivePropertySlim<Avalonia.Media.Geometry?> PreviewPath { get; } = new(null);

    private void OnFigureInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        _invalidated = true;
    }

    public void RecreatePreviewPath()
    {
        if (!_invalidated) return;

        try
        {
            if (Value.Value != null)
            {
                using (var context = new GeometryContext())
                {
                    Value.Value.ApplyTo(context);
                    string path = context.NativeObject.ToSvgPathData();
                    PreviewPath.Value = Avalonia.Media.Geometry.Parse(path);
                    return;
                }
            }
        }
        catch
        {
            PreviewPath.Value = null;
        }
        finally
        {
            _invalidated = false;
        }
    }

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
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            group.Segments.BeginRecord<PathSegment>()
                .Add(instance)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
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

    protected override void Dispose(bool disposing)
    {
        if (Value.Value != null)
        {
            Value.Value.Invalidated -= OnFigureInvalidated;
        }
        if (_editViewModel.Value is { } editViewModel)
        {
            PathEditorTabViewModel? tab = editViewModel.FindToolTab<PathEditorTabViewModel>();
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
