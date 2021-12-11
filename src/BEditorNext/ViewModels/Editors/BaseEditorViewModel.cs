using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels.Editors;

public abstract class BaseEditorViewModel
{
    protected BaseEditorViewModel(ISetter setter)
    {
        Setter = setter;
    }

    public ISetter Setter { get; }

    public bool CanReset => Setter.Property.MetaTable.ContainsKey(PropertyMetaTableKeys.DefaultValue);

    public virtual string Header => Setter.Property.GetValueOrDefault(
        PropertyMetaTableKeys.Header,
        Setter.Property.GetValueOrDefault(
            PropertyMetaTableKeys.JsonName,
            "Unknown"));
}

public abstract class BaseEditorViewModel<T> : BaseEditorViewModel
{
    protected BaseEditorViewModel(Setter<T> setter)
        : base(setter)
    {
    }

    public new Setter<T> Setter => (Setter<T>)base.Setter;

    public void Reset()
    {
        if (CanReset)
        {
            Setter.Value = Setter.Property.GetDefaultValue();
        }
    }

    public void SetValue(T? oldValue, T? newValue)
    {
        CommandRecorder.Default.DoAndPush(new SetCommand(Setter, oldValue, newValue));
    }

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly Setter<T> _setter;
        private readonly T? _oldValue;
        private readonly T? _newValue;

        public SetCommand(Setter<T> setter, T? oldValue, T? newValue)
        {
            _setter = setter;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _setter.Value = _newValue;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _setter.Value = _oldValue;
        }
    }
}
