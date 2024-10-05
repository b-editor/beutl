using System.Text.Json.Nodes;

using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Services;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class PenEditorViewModel : BaseEditorViewModel
{
    public PenEditorViewModel(IPropertyAdapter property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x as IPen)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);

        Value.Subscribe(Update)
            .DisposeWith(Disposables);
    }

    private void Update(IPen? pen)
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
        if (pen is Pen mutablePen)
        {
            using var props = new PooledList<IPropertyAdapter>();
            Span<IPropertyAdapter> span = props.AddSpan(4);
            span[0] = new AnimatablePropertyAdapter<float>(Pen.ThicknessProperty, mutablePen);
            span[1] = new AnimatablePropertyAdapter<StrokeJoin>(Pen.StrokeJoinProperty, mutablePen);
            span[2] = new AnimatablePropertyAdapter<StrokeAlignment>(Pen.StrokeAlignmentProperty, mutablePen);
            span[3] = new AnimatablePropertyAdapter<IBrush?>(Pen.BrushProperty, mutablePen);

            CreateContexts(props, MajorProperties);

            props.Clear();
            span = props.AddSpan(4);
            span[0] = new AnimatablePropertyAdapter<float>(Pen.MiterLimitProperty, mutablePen);
            span[1] = new AnimatablePropertyAdapter<StrokeCap>(Pen.StrokeCapProperty, mutablePen);
            span[2] = new AnimatablePropertyAdapter<CoreList<float>?>(Pen.DashArrayProperty, mutablePen);
            span[3] = new AnimatablePropertyAdapter<float>(Pen.DashOffsetProperty, mutablePen);

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

    public ReadOnlyReactiveProperty<IPen?> Value { get; }

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
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            IPropertyAdapter prop = PropertyAdapter;

            RecordableCommands.Create(GetStorables())
                .OnDo(() => prop.SetValue(newValue))
                .OnUndo(() => prop.SetValue(oldValue))
                .ToCommand()
                .DoAndRecord(recorder);
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
