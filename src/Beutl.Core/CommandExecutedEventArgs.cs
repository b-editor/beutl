namespace Beutl;

public class CommandExecutedEventArgs(IRecordableCommand command, CommandType type) : EventArgs
{
    public IRecordableCommand Command { get; } = command;

    public CommandType Type { get; } = type;
}
