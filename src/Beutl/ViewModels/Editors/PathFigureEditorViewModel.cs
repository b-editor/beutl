using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Operation;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Editors;

public sealed class PathFigureEditorViewModel : ValueEditorViewModel<PathFigure>
{
    public PathFigureEditorViewModel(IAbstractProperty<PathFigure> property)
        : base(property)
    {
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
    }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<PathSegment>?> Group { get; } = new();

    public ReactivePropertySlim<GeometryEditorViewModel?> ParentContext { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
        if (visitor is IServiceProvider serviceProvider)
        {
            ParentContext.Value = serviceProvider.GetService<GeometryEditorViewModel>();
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
