namespace BEditorNext;

public class CommandExecutedEventArgs : EventArgs
{
    public CommandExecutedEventArgs(IRecordableCommand command, CommandType type)
    {
        Command = command;
        Type = type;
    }

    public IRecordableCommand Command { get; }

    public CommandType Type { get; }
}
