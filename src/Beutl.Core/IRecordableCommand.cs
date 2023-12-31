namespace Beutl;

public interface IRecordableCommand
{
    void Do();

    void Undo();

    void Redo();
}

public static class RecordableCommandExtensions
{
    public static void DoAndRecord(this IRecordableCommand command, CommandRecorder recorder)
    {
        recorder.DoAndPush(command);
    }

    public static void PushTo(this IRecordableCommand command, CommandRecorder recorder)
    {
        recorder.PushOnly(command);
    }

    public static IRecordableCommand Append(this IRecordableCommand command1, IRecordableCommand command2)
    {
        return new ConnectedCommand(command1, command2);
    }

    public static IRecordableCommand ToCommand(this IRecordableCommand[] commands)
    {
        return new MultipleCommand(commands);
    }

    private sealed class ConnectedCommand(IRecordableCommand command1, IRecordableCommand command2) : IRecordableCommand
    {
        public void Do()
        {
            command1.Do();
            command2.Do();
        }

        public void Redo()
        {
            command1.Redo();
            command2.Redo();
        }

        public void Undo()
        {
            command1.Undo();
            command2.Undo();
        }
    }

    private sealed class MultipleCommand(IRecordableCommand[] commands) : IRecordableCommand
    {
        public void Do()
        {
            for (int i = 0; i < commands.Length; i++)
            {
                IRecordableCommand? item = commands[i];
                item.Do();
            }
        }

        public void Redo()
        {
            for (int i = 0; i < commands.Length; i++)
            {
                IRecordableCommand? item = commands[i];
                item.Redo();
            }
        }

        public void Undo()
        {
            for (int i = commands.Length - 1; i >= 0; i--)
            {
                IRecordableCommand? item = commands[i];
                item.Undo();
            }
        }
    }
}
