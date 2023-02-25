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

    private sealed class ConnectedCommand : IRecordableCommand
    {
        private readonly IRecordableCommand _command1;
        private readonly IRecordableCommand _command2;

        public ConnectedCommand(IRecordableCommand command1, IRecordableCommand command2)
        {
            _command1 = command1;
            _command2 = command2;
        }

        public void Do()
        {
            _command1.Do();
            _command2.Do();
        }

        public void Redo()
        {
            _command1.Redo();
            _command2.Redo();
        }

        public void Undo()
        {
            _command1.Undo();
            _command2.Undo();
        }
    }

    private sealed class MultipleCommand : IRecordableCommand
    {
        private readonly IRecordableCommand[] _commands;

        public MultipleCommand(IRecordableCommand[] commands)
        {
            _commands = commands;
        }

        public void Do()
        {
            for (int i = 0; i < _commands.Length; i++)
            {
                IRecordableCommand? item = _commands[i];
                item.Do();
            }
        }

        public void Redo()
        {
            for (int i = 0; i < _commands.Length; i++)
            {
                IRecordableCommand? item = _commands[i];
                item.Redo();
            }
        }

        public void Undo()
        {
            for (int i = _commands.Length - 1; i >= 0; i--)
            {
                IRecordableCommand? item = _commands[i];
                item.Undo();
            }
        }
    }
}
