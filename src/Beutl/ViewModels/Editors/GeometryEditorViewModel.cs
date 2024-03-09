using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Operation;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Editors;

public sealed class GeometryEditorViewModel : ValueEditorViewModel<Geometry?>
{
    public GeometryEditorViewModel(IAbstractProperty<Geometry?> property)
        : base(property)
    {
        IsGroup = Value.Select(v => v is PathGeometry)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is PathGeometry || v == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                {
                    Properties.Value?.Dispose();
                    Properties.Value = null;
                    Group.Value?.Dispose();
                    Group.Value = null;

                    if (v is PathGeometry group)
                    {
                        var prop = new CorePropertyImpl<PathFigures>(PathGeometry.FiguresProperty, group);
                        Group.Value = new ListEditorViewModel<PathFigure>(prop)
                        {
                            IsExpanded = { Value = true }
                        };

                        Properties.Value = new PropertiesEditorViewModel(group,
                            (p, m) => p == Geometry.FillTypeProperty);
                    }
                    else if (v is Geometry geometry)
                    {
                        Properties.Value = new PropertiesEditorViewModel(geometry, (p, m) => m.Browsable && p != Geometry.TransformProperty);
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

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<PathFigure>?> Group { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
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

    public void ChangeGeometryType(Type type)
    {
        if (Activator.CreateInstance(type) is Geometry instance)
        {
            SetValue(Value.Value, instance);
        }
    }

    public void AddItem()
    {
        if (Value.Value is PathGeometry group)
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            group.Figures.BeginRecord<PathFigure>()
                .Add(new PathFigure())
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
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }

    private sealed record Visitor(GeometryEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(GeometryEditorViewModel))
                return Obj;

            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
