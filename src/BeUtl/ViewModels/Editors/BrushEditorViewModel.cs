using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public class BrushEditorViewModel : BaseEditorViewModel
{
    private static readonly NullabilityInfoContext s_context = new();

    public BrushEditorViewModel(IWrappedProperty property)
        : base(property)
    {
        CoreProperty coreProperty = property.AssociatedProperty;
        PropertyInfo propertyInfo = coreProperty.OwnerType.GetProperty(coreProperty.Name)!;
        NullabilityInfo? nullabilityInfo = s_context.Create(propertyInfo);

        CanWrite = propertyInfo.SetMethod?.IsPublic == true;
        CanDelete = CanWrite && nullabilityInfo.WriteState == NullabilityState.Nullable;

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
    }

    public ReadOnlyReactivePropertySlim<IBrush?> Value { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSet { get; }

    public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    public bool CanWrite { get; }

    public bool CanDelete { get; }

    public void SetValue(IBrush? oldValue, IBrush? newValue)
    {
        if (!EqualityComparer<IBrush>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder.Default.DoAndPush(new SetCommand(WrappedProperty, oldValue, newValue));
        }
    }

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly IWrappedProperty _setter;
        private readonly object? _oldValue;
        private readonly object? _newValue;

        public SetCommand(IWrappedProperty setter, object? oldValue, object? newValue)
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
