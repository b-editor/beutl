using Beutl.Framework;
using Beutl.Media;
using Beutl.ViewModels.Tools;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SetCommand : IRecordableCommand
{
    private readonly IAbstractProperty _setter;
    private readonly object? _oldValue;
    private readonly object? _newValue;

    public SetCommand(IAbstractProperty setter, object? oldValue, object? newValue)
    {
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Do()
    {
        _setter.SetValue(_newValue);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        _setter.SetValue(_oldValue);
    }
}

public sealed class BrushEditorViewModel : BaseEditorViewModel
{
    private bool _accepted;

    public BrushEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x as IBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ChildContext = Value.Select(v => v as ICoreObject)
            .Select(x => x != null ? new PropertiesEditorViewModel(x, m => m.Browsable) : null)
            .Do(AcceptChildren)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsSolid = Value.Select(v => v is ISolidColorBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsLinearGradient = Value.Select(v => v is ILinearGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsConicGradient = Value.Select(v => v is IConicGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsRadialGradient = Value.Select(v => v is IRadialGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    private void AcceptChildren(PropertiesEditorViewModel? obj)
    {
        _accepted = false;

        if (obj != null)
        {
            var visitor = new Visitor(this);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }

            _accepted = true;
        }
    }

    public ReadOnlyReactivePropertySlim<IBrush?> Value { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> ChildContext { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSolid { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLinearGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsConicGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRadialGradient { get; }

    public ReactivePropertySlim<bool> IsSeparatorVisible { get; } = new();

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(Value.Value, (IBrush?)defaultValue);
        }
    }

    public void SetValue(IBrush? oldValue, IBrush? newValue)
    {
        if (!EqualityComparer<IBrush>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder.Default.DoAndPush(new SetCommand(WrappedProperty, oldValue, newValue));
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider && !_accepted)
        {
            AcceptChildren(ChildContext.Value);
        }

        IsSeparatorVisible.Value = visitor is SourceOperatorViewModel;
    }

    private sealed record Visitor(BrushEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
