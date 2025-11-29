using System.Collections.Immutable;

namespace Beutl;

public sealed class RecordableCommandBuilder(ImmutableArray<CoreObject?> storables)
{
    private readonly ImmutableArray<CoreObject?> _storables = storables;
    private Action? _do;
    private Action? _undo;
    private Action? _redo;

    public RecordableCommandBuilder OnDo(Action action, bool redo = true)
    {
        _do += action;
        if (redo)
            _redo += action;

        return this;
    }

    public RecordableCommandBuilder OnUndo(Action action)
    {
        _undo += action;
        return this;
    }


    public RecordableCommandBuilder OnRedo(Action action)
    {
        _redo += action;
        return this;
    }

    public IRecordableCommand ToCommand(ImmutableArray<CoreObject?> storables)
    {
        Action empty = () => { };
        return RecordableCommands.Create(_do ?? empty, _undo ?? empty, [.. _storables, .. storables], _redo);
    }

    public IRecordableCommand ToCommand()
    {
        Action empty = () => { };
        return RecordableCommands.Create(_do ?? empty, _undo ?? empty, _storables, _redo);
    }
}
