using Beutl.Styling;

namespace Beutl;

public sealed class ChangeSetterValueCommand<T>(Setter<T> setter, T oldValue, T newValue) : IRecordableCommand
{
    public void Do()
    {
        setter.Value = newValue;
    }

    public void Redo() => Do();

    public void Undo()
    {
        setter.Value = oldValue;
    }
}
