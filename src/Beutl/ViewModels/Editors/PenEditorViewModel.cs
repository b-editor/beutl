using Avalonia.Collections.Pooled;

using Beutl.Framework;
using Beutl.Media;
using Beutl.Operators.Configure;
using Beutl.Services;
using Beutl.ViewModels.Tools;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class PenEditorViewModel : BaseEditorViewModel
{
    private bool _accepted;

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
        void CreateContexts(PooledList<CoreProperty> props, CoreList<IPropertyEditorContext> dst)
        {
            CoreProperty[]? foundItems;
            PropertyEditorExtension? extension;

            do
            {
                (foundItems, extension) = PropertyEditorService.MatchProperty(props);
                if (foundItems != null && extension != null)
                {
                    var tmp = new IAbstractProperty[foundItems.Length];
                    for (int i = 0; i < foundItems.Length; i++)
                    {
                        CoreProperty item = foundItems[i];
                        Type wrapperGType = typeof(AnimatableCorePropertyImpl<>).MakeGenericType(item.PropertyType);
                        tmp[i] = (IAbstractProperty)Activator.CreateInstance(wrapperGType, item, pen)!;
                    }

                    if (extension.TryCreateContext(tmp, out IPropertyEditorContext? context))
                    {
                        dst.Add(context);
                    }

                    props.RemoveMany(foundItems);
                }
            } while (foundItems != null && extension != null);
        }

        MajorProperties.Clear();
        MinorProperties.Clear();
        if (pen is Pen)
        {
            using var props = new PooledList<CoreProperty>();
            Span<CoreProperty> span = props.AddSpan(4);
            span[0] = Pen.ThicknessProperty;
            span[1] = Pen.StrokeCapProperty;
            span[2] = Pen.StrokeAlignmentProperty;
            span[3] = Pen.BrushProperty;

            CreateContexts(props, MajorProperties);

            props.Clear();
            span = props.AddSpan(4);
            span[0] = Pen.MiterLimitProperty;
            span[1] = Pen.StrokeJoinProperty;
            span[2] = Pen.DashArrayProperty;
            span[3] = Pen.DashOffsetProperty;

            CreateContexts(props, MinorProperties);
        }

        AcceptChildren();
    }

    private void AcceptChildren()
    {
        _accepted = false;

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

            _accepted = true;
        }
    }

    public ReadOnlyReactivePropertySlim<IPen?> Value { get; }

    public CoreList<IPropertyEditorContext> MajorProperties { get; } = new();

    public CoreList<IPropertyEditorContext> MinorProperties { get; } = new();

    public ReactivePropertySlim<bool> IsSeparatorVisible { get; } = new();

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
        if (visitor is IServiceProvider && !_accepted)
        {
            AcceptChildren();
        }

        IsSeparatorVisible.Value = visitor is SourceOperatorViewModel;
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
