using System.Text.Json.Nodes;

using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Serialization;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class GeometryEditorViewModel : ValueEditorViewModel<Geometry?>, IGeometryEditorContext, IFallbackObjectViewModel
{
    public GeometryEditorViewModel(IPropertyAdapter<Geometry?> property)
        : base(property)
    {
        IsFallback = Value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(Disposables);

        FallbackMessage = Value.Select(FallbackHelper.GetFallbackMessage)
            .ToReadOnlyReactivePropertySlim(MessageStrings.RestoreFailedTypeNotFound)
            .DisposeWith(Disposables);

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
                        var prop = new EnginePropertyAdapter<ICoreList<PathFigure>>(group.Figures, group);
                        Group.Value = new ListEditorViewModel<PathFigure>(prop)
                        {
                            IsExpanded = { Value = true }
                        };

                        Properties.Value = new PropertiesEditorViewModel(group,
                            p => p == group.FillType);
                    }
                    else if (v is { } geometry)
                    {
                        Properties.Value = new PropertiesEditorViewModel(geometry, (p) => p != geometry.Transform);
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

    }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<PathFigure>?> Group { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString()
    {
        return Value.Select(v =>
        {
            if (v is FallbackGeometry { Json: JsonObject json })
            {
                return json.ToJsonString(JsonHelper.SerializerOptions);
            }

            return null;
        });
    }

    public void SetJsonString(string? str)
    {
        string message = MessageStrings.InvalidJson;
        _ = str ?? throw new Exception(message);
        JsonObject json = (JsonNode.Parse(str) as JsonObject) ?? throw new Exception(message);

        Type? type = json.GetDiscriminator();
        Geometry? instance = null;
        if (type?.IsAssignableTo(typeof(Geometry)) ?? false)
        {
            instance = Activator.CreateInstance(type) as Geometry;
        }

        if (instance == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(instance, type!, json);

        SetValue(Value.Value, instance);
    }

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
            group.Figures.Add(new PathFigure());
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }

    public void ExpandForEditing()
    {
        if (!IsExpanded.Value)
        {
            IsExpanded.Value = true;
        }
    }

    public IPathFigureEditorContext? FindPathFigureContext(PathFigure figure)
    {
        return Group.Value?.Items
            .FirstOrDefault(v => v.Context is PathFigureEditorViewModel f && f.Value.Value == figure)
            ?.Context as IPathFigureEditorContext;
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
