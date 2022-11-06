using System.ComponentModel;
using System.Diagnostics;

namespace Beutl;

public enum CommandType
{
    Do,

    Undo,

    Redo,
}

public class CommandRecorder : INotifyPropertyChanged
{
    public static readonly CommandRecorder Default = new();
    private static readonly PropertyChangedEventArgs s_canUndoArgs = new(nameof(CanUndo));
    private static readonly PropertyChangedEventArgs s_canRedoArgs = new(nameof(CanRedo));
    private static readonly PropertyChangedEventArgs s_lastExecutedTimeArgs = new(nameof(LastExecutedTime));
    private readonly Stack<IRecordableCommand> _undoStack = new();
    private readonly Stack<IRecordableCommand> _redoStack = new();
    private bool _isExecuting;
    private bool _canUndo;
    private bool _canRedo;
    private DateTime _lastExecutedTime;

    public DateTime LastExecutedTime
    {
        get => _lastExecutedTime;
        private set
        {
            if (_lastExecutedTime != value)
            {
                _lastExecutedTime = value;
                PropertyChanged?.Invoke(this, s_lastExecutedTimeArgs);
            }
        }
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set
        {
            if (_canUndo != value)
            {
                _canUndo = value;
                PropertyChanged?.Invoke(this, s_canUndoArgs);
            }
        }
    }

    public bool CanRedo
    {
        get => _canRedo;
        private set
        {
            if (_canRedo != value)
            {
                _canRedo = value;
                PropertyChanged?.Invoke(this, s_canRedoArgs);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<CommandExecutedEventArgs>? Executed;

    public event EventHandler? Canceled;

    public event EventHandler? Cleared;

    public void PushOnly(IRecordableCommand command)
    {
        _undoStack.Push(command);
        CanUndo = _undoStack.Count > 0;

        _redoStack.Clear();
        CanRedo = _redoStack.Count > 0;

        LastExecutedTime = DateTime.UtcNow;
        Executed?.Invoke(command, new(command, CommandType.Do));
    }

    public void DoAndPush(IRecordableCommand command)
    {
        if (_isExecuting)
        {
            Debug.WriteLine("if (!process) {...");
            return;
        }

        try
        {
            _isExecuting = true;
            command.Do();

            _undoStack.Push(command);
            CanUndo = _undoStack.Count > 0;

            _redoStack.Clear();
            CanRedo = _redoStack.Count > 0;
        }
        catch
        {
            Debug.Fail("Commandの実行中に例外が発生。");
            Canceled?.Invoke(null, EventArgs.Empty);
        }

        _isExecuting = false;
        LastExecutedTime = DateTime.UtcNow;
        Executed?.Invoke(command, new(command, CommandType.Do));
    }

    public void Undo()
    {
        if (_isExecuting) return;

        if (_undoStack.Count >= 1)
        {
            IRecordableCommand command = _undoStack.Pop();
            CanUndo = _undoStack.Count > 0;

            try
            {
                _isExecuting = true;
                command.Undo();

                _redoStack.Push(command);
                CanRedo = _redoStack.Count > 0;
            }
            catch
            {
            }

            _isExecuting = false;
            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(command, new(command, CommandType.Undo));
        }
    }

    public void Redo()
    {
        if (_isExecuting) return;

        if (_redoStack.Count >= 1)
        {
            IRecordableCommand command = _redoStack.Pop();
            CanRedo = _redoStack.Count > 0;

            try
            {
                _isExecuting = true;
                command.Redo();

                _undoStack.Push(command);
                CanUndo = _undoStack.Count > 0;
            }
            catch
            {
            }

            _isExecuting = false;
            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(command, new(command, CommandType.Redo));
        }
    }

    public void Clear()
    {
        LastExecutedTime = DateTime.MinValue;
        _undoStack.Clear();
        _redoStack.Clear();
        CanUndo = false;
        CanRedo = false;

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
