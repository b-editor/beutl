using System.Collections.Immutable;

namespace Beutl;

public interface IRecordableCommand
{
    bool Nothing => false;

    ImmutableArray<IStorable?> GetStorables() => [];

    void Do();

    void Undo();

    void Redo();
}

public static partial class RecordableCommands
{
    public static void DoAndRecord(this IRecordableCommand command, CommandRecorder recorder)
    {
        recorder.DoAndPush(command);
    }

    public static void PushTo(this IRecordableCommand command, CommandRecorder recorder)
    {
        recorder.PushOnly(command);
    }

    public static IRecordableCommand Append(this IRecordableCommand? command1, IRecordableCommand? command2)
    {
        if (command2 != null)
        {
            if (command1 != null)
            {
                return new ConnectedCommand(command1, command2);
            }
            else
            {
                return command2;
            }
        }
        else
        {
            return command1 ?? EmptyCommand.Instance;
        }
    }

    public static IRecordableCommand ToCommand(this IRecordableCommand[] commands)
    {
        return new MultipleCommand(commands, []);
    }

    public static IRecordableCommand ToCommand(this IRecordableCommand[] commands, ImmutableArray<IStorable?> storables)
    {
        return new MultipleCommand(commands, storables);
    }

    public static IRecordableCommand WithStoables(this IRecordableCommand command, ImmutableArray<IStorable?> storables, bool overwrite = false)
    {
        return new WithStoableCommand(command, storables, overwrite);
    }

    private sealed class WithStoableCommand(
        IRecordableCommand command, ImmutableArray<IStorable?> storables, bool overwrite) : IRecordableCommand
    {
        public bool Nothing => command.Nothing;

        public ImmutableArray<IStorable?> GetStorables()
        {
            if (overwrite)
            {
                return storables;
            }
            else
            {
                return command.GetStorables()
                    .Concat(storables)
                    .ToImmutableArray();
            }
        }

        public void Do()
        {
            command.Do();
        }

        public void Redo()
        {
            command.Redo();
        }

        public void Undo()
        {
            command.Undo();
        }
    }

    private sealed class ConnectedCommand(IRecordableCommand command1, IRecordableCommand command2) : IRecordableCommand
    {
        public bool Nothing
        {
            get
            {
                IRecordableCommand[] items = [command1, command2];
                return items.All(item => item.Nothing);
            }
        }

        public ImmutableArray<IStorable?> GetStorables()
        {
            return command1.GetStorables()
                .Concat(command2.GetStorables())
                .ToImmutableArray();
        }

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

    private sealed class MultipleCommand(IRecordableCommand[] commands, ImmutableArray<IStorable?> storables) : IRecordableCommand
    {
        public bool Nothing
        {
            get
            {
                if (commands.Length == 0)
                    return true;

                return commands.All(item => item.Nothing);
            }
        }

        public ImmutableArray<IStorable?> GetStorables()
        {
            return commands.SelectMany(v => v.GetStorables())
                .Concat(storables)
                .ToImmutableArray();
        }

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

    private sealed class EmptyCommand : IRecordableCommand
    {
        public static readonly EmptyCommand Instance = new();

        public bool Nothing => true;

        public ImmutableArray<IStorable?> GetStorables()
        {
            return [];
        }

        public void Do()
        {
        }

        public void Redo()
        {
        }

        public void Undo()
        {
        }
    }
}
