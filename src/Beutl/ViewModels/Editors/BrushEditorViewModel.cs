using System.Reflection;

using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class BrushEditorViewModel : BaseEditorViewModel
{
    private static readonly NullabilityInfoContext s_context = new();
    private bool _accepted;

    public BrushEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        CoreProperty coreProperty = property.Property;
        PropertyInfo propertyInfo = coreProperty.OwnerType.GetProperty(coreProperty.Name)!;
        NullabilityInfo? nullabilityInfo = s_context.Create(propertyInfo);

        CanWrite = propertyInfo.SetMethod?.IsPublic == true;
        CanDelete = (CanWrite && nullabilityInfo.WriteState == NullabilityState.Nullable)
            || IsStylingSetter;

        IsSet = property.GetObservable()
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsNotSetAndCanWrite = IsSet.Select(x => !x && CanWrite)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value = property.GetObservable()
            .Select(x => x as IBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ChildContext = Value.Select(v => v as ICoreObject)
            .Select(x => x != null ? new PropertiesEditorViewModel(x, m => m.Browsable) : null)
            .Do(AcceptChildren)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    private void AcceptChildren(PropertiesEditorViewModel? obj)
    {
        _accepted = false;

        EditViewModel? editViewModel = GetEditViewModel();
        if (obj != null && editViewModel != null)
        {
            var visitor = new Visitor(editViewModel);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }

            _accepted = true;
        }
    }

    public ReadOnlyReactivePropertySlim<IBrush?> Value { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> ChildContext { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSet { get; }

    public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    public bool CanWrite { get; }

    public bool CanDelete { get; }

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
        if (visitor is IProvideEditViewModel && !_accepted)
        {
            AcceptChildren(ChildContext.Value);
        }
    }

    private EditViewModel? GetEditViewModel()
    {
        return ((IProvideEditViewModel)this).EditViewModel;
    }

    private sealed record Visitor(EditViewModel EditViewModel) : IProvideEditViewModel, IPropertyEditorContextVisitor
    {
        public void Visit(IPropertyEditorContext context)
        {
        }
    }

    private sealed class SetCommand : IRecordableCommand
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
}
