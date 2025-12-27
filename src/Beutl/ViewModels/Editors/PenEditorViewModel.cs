using System.Text.Json.Nodes;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Services;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class PenEditorViewModel : BaseEditorViewModel
{
    public PenEditorViewModel(IPropertyAdapter<Pen?> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);

        Value.Subscribe(Update)
            .DisposeWith(Disposables);
    }

    private void Update(Pen? pen)
    {
        static void CreateContexts(PooledList<IPropertyAdapter> props, CoreList<IPropertyEditorContext> dst)
        {
            IPropertyAdapter[]? foundItems;
            PropertyEditorExtension? extension;

            do
            {
                (foundItems, extension) = PropertyEditorService.MatchProperty(props);
                if (foundItems != null && extension != null)
                {
                    if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                    {
                        dst.Add(context);
                    }

                    props.RemoveMany(foundItems);
                }
            } while (foundItems != null && extension != null);
        }

        MajorProperties.Clear();
        MinorProperties.Clear();
        if (pen != null)
        {
            using var props = new PooledList<IPropertyAdapter>();
            Span<IPropertyAdapter> span = props.AddSpan(4);
            span[0] = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)pen.Thickness, pen);
            span[1] = new EnginePropertyAdapter<StrokeJoin>(pen.StrokeJoin, pen);
            span[2] = new EnginePropertyAdapter<StrokeAlignment>(pen.StrokeAlignment, pen);
            span[3] = new EnginePropertyAdapter<Brush?>(pen.Brush, pen);

            CreateContexts(props, MajorProperties);

            props.Clear();
            span = props.AddSpan(4);
            span[0] = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)pen.MiterLimit, pen);
            span[1] = new EnginePropertyAdapter<StrokeCap>(pen.StrokeCap, pen);
            span[2] = new EnginePropertyAdapter<CoreList<float>?>(pen.DashArray, pen);
            span[3] = new EnginePropertyAdapter<float>(pen.DashOffset, pen);

            CreateContexts(props, MinorProperties);
        }

        AcceptChildren();
    }

    private void AcceptChildren()
    {
        if (Value.Value != null)
        {
            var visitor = new Visitor(this);
            foreach (IPropertyEditorContext item in MajorProperties)
            {
                item.Accept(visitor);
            }
            foreach (IPropertyEditorContext item in MinorProperties)
            {
                item.Accept(visitor);
            }
        }
    }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    public ReadOnlyReactiveProperty<Pen?> Value { get; }

    public CoreList<IPropertyEditorContext> MajorProperties { get; } = [];

    public CoreList<IPropertyEditorContext> MinorProperties { get; } = [];

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(Value.Value, (Pen?)defaultValue);
        }
    }

    public void SetValue(Pen? oldValue, Pen? newValue)
    {
        if (!EqualityComparer<Pen>.Default.Equals(oldValue, newValue))
        {
            PropertyAdapter.SetValue(newValue);
            Commit();
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);

        if (visitor is IServiceProvider)
        {
            AcceptChildren();
        }
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue(nameof(IsExpanded), out JsonNode? isExpandedNode)
            && isExpandedNode is JsonValue isExpanded)
        {
            IsExpanded.Value = (bool)isExpanded;
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (IPropertyEditorContext item in MajorProperties)
        {
            item.Dispose();
        }
        foreach (IPropertyEditorContext item in MinorProperties)
        {
            item.Dispose();
        }
    }

    private sealed record Visitor(PenEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
