using System.Text.Json.Nodes;

using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Operators.Configure;
using Beutl.Services;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class PenEditorViewModel : BaseEditorViewModel
{
    public PenEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x as IPen)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value.Subscribe(Update)
            .DisposeWith(Disposables);
    }

    private void Update(IPen? pen)
    {
        static void CreateContexts(PooledList<IAbstractProperty> props, CoreList<IPropertyEditorContext> dst)
        {
            IAbstractProperty[]? foundItems;
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
        if (pen is Pen mutablePen)
        {
            using var props = new PooledList<IAbstractProperty>();
            Span<IAbstractProperty> span = props.AddSpan(4);
            span[0] = new AnimatableCorePropertyImpl<float>(Pen.ThicknessProperty, mutablePen);
            span[1] = new AnimatableCorePropertyImpl<StrokeJoin>(Pen.StrokeJoinProperty, mutablePen);
            span[2] = new AnimatableCorePropertyImpl<StrokeAlignment>(Pen.StrokeAlignmentProperty, mutablePen);
            span[3] = new AnimatableCorePropertyImpl<IBrush?>(Pen.BrushProperty, mutablePen);

            CreateContexts(props, MajorProperties);

            props.Clear();
            span = props.AddSpan(4);
            span[0] = new AnimatableCorePropertyImpl<float>(Pen.MiterLimitProperty, mutablePen);
            span[1] = new AnimatableCorePropertyImpl<StrokeCap>(Pen.StrokeCapProperty, mutablePen);
            span[2] = new AnimatableCorePropertyImpl<CoreList<float>?>(Pen.DashArrayProperty, mutablePen);
            span[3] = new AnimatableCorePropertyImpl<float>(Pen.DashOffsetProperty, mutablePen);

            CreateContexts(props, MinorProperties);
        }

        AcceptChildren();
    }

    private void AcceptChildren()
    {
        if (Value.Value is Pen)
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

    public ReadOnlyReactivePropertySlim<IPen?> Value { get; }

    public CoreList<IPropertyEditorContext> MajorProperties { get; } = [];

    public CoreList<IPropertyEditorContext> MinorProperties { get; } = [];

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(Value.Value, (IPen?)defaultValue);
        }
    }

    public void SetValue(IPen? oldValue, IPen? newValue)
    {
        if (!EqualityComparer<IPen>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder.Default.DoAndPush(new SetCommand(WrappedProperty, oldValue, newValue));
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
