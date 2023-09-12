using Beutl.Styling;

namespace Beutl;

public sealed class ChangeSetterValueCommand<T> : IRecordableCommand
{
    private readonly Setter<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;

    public ChangeSetterValueCommand(Setter<T> setter, T oldValue, T newValue)
    {
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Do()
    {
        _setter.Value = _newValue;
    }

    public void Redo() => Do();

    public void Undo()
    {
        _setter.Value = _oldValue;
    }
}
