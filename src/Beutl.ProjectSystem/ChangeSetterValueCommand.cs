using System.Collections.Immutable;

using Beutl.Styling;

namespace Beutl;

public sealed class ChangeSetterValueCommand<T>(
    Setter<T> setter, T oldValue, T newValue, ImmutableArray<IStorable?> storables) : IRecordableCommand
{
    public ImmutableArray<IStorable?> GetStorables() => storables;

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
