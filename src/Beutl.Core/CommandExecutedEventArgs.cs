using System.Collections.Immutable;

namespace Beutl;

public class CommandExecutedEventArgs(IRecordableCommand command, CommandType type, ImmutableHashSet<CoreObject> storables) : EventArgs
{
    public IRecordableCommand Command { get; } = command;

    public CommandType Type { get; } = type;

    public ImmutableHashSet<CoreObject> Storables { get; } = storables;
}
