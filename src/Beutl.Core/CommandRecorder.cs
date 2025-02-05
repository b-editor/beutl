using System.Collections.Immutable;
using System.ComponentModel;

using Beutl.Language;
using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;

namespace Beutl;

public enum CommandType
{
    Do,

    Undo,

    Redo,
}

internal record Entry(IRecordableCommand Command, ImmutableHashSet<IStorable> Storables);

public class CommandRecorder : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_canUndoArgs = new(nameof(CanUndo));
    private static readonly PropertyChangedEventArgs s_canRedoArgs = new(nameof(CanRedo));
    private static readonly PropertyChangedEventArgs s_lastExecutedTimeArgs = new(nameof(LastExecutedTime));
    private readonly ILogger<CommandRecorder> _logger = Log.CreateLogger<CommandRecorder>();
    private readonly CommandStack<Entry> _undoStack = new(20000);
    private readonly CommandStack<Entry> _redoStack = new(20000);
    private readonly SemaphoreSlim _semaphoreSlim = new(1);
    private bool _canUndo;
    private bool _canRedo;
    private DateTime _lastExecutedTime;
    private IRecordableCommand? _executingCommand;

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

    private static Entry CreateEntry(IRecordableCommand command)
    {
        return new Entry(command, command.GetStorables().Where(v => v != null).ToImmutableHashSet()!);
    }

    private Entry? CreateEntryAndCheck(IRecordableCommand command)
    {
        if (command.Nothing)
        {
            _logger.LogInformation("Command '{CommandType}' has nothing to record.", command.GetType());
            return null;
        }

        Entry entry = CreateEntry(command);
        if (entry.Storables.Count == 0)
        {
            _logger.LogWarning("Command '{CommandType}' has no storables.", command.GetType());
        }

        return entry;
    }

    private bool SemaphoreWait()
    {
        if (!_semaphoreSlim.Wait(1000))
        {
            _logger.LogWarning("SemaphoreSlim wait timeout. Currently executing command: '{Command}'", _executingCommand);
            NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            Canceled?.Invoke(null, EventArgs.Empty);
            return false;
        }
        else
        {
            return true;
        }
    }

    public void PushOnly(IRecordableCommand command)
    {
        _logger.LogDebug("Attempting to push command '{CommandType}' to undo stack.", command.GetType());

        if (!SemaphoreWait())
        {
            return;
        }

        try
        {
            if (CreateEntryAndCheck(command) is not { } entry)
                return;

            _undoStack.Push(entry);
            CanUndo = _undoStack.Count > 0;

            _redoStack.Clear();
            CanRedo = _redoStack.Count > 0;

            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(command, new(command, CommandType.Do, entry.Storables));
        }
        finally
        {
            _semaphoreSlim.Release();
        }

        _logger.LogInformation("Command '{CommandType}' pushed to undo stack.", command.GetType());
    }

    public void DoAndPush(IRecordableCommand command)
    {
        _logger.LogDebug("Executing and pushing command '{CommandType}' to undo stack.", command.GetType());

        if (!SemaphoreWait())
        {
            return;
        }

        Entry? entry = null;
        try
        {
            if (CreateEntryAndCheck(command) is not { } entry1)
                return;
            entry = entry1;

            _executingCommand = command;
            command.Do();

            _undoStack.Push(entry);
            CanUndo = _undoStack.Count > 0;

            _redoStack.Clear();
            CanRedo = _redoStack.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while executing command '{CommandType}'.", command.GetType());
            NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            Canceled?.Invoke(null, EventArgs.Empty);
        }
        finally
        {
            _semaphoreSlim.Release();
            _executingCommand = null;
        }

        LastExecutedTime = DateTime.UtcNow;
        Executed?.Invoke(command, new(command, CommandType.Do, entry!.Storables));

        _logger.LogInformation("Command '{CommandType}' executed and pushed to undo stack.", command.GetType());
    }

    public void Undo()
    {
        _logger.LogDebug("Attempting to undo last command.");

        if (_undoStack.Count >= 1)
        {
            if (!SemaphoreWait())
            {
                return;
            }

            Entry? entry = _undoStack.Pop();
            if (entry == null)
            {
                _logger.LogWarning("Undo stack is empty.");
                _semaphoreSlim.Release();
                return;
            }

            CanUndo = _undoStack.Count > 0;

            try
            {
                _executingCommand = entry.Command;
                entry.Command.Undo();

                _redoStack.Push(entry);
                CanRedo = _redoStack.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while undoing command '{CommandType}'.", entry.Command.GetType());
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
            finally
            {
                _semaphoreSlim.Release();
                _executingCommand = null;
            }

            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(entry.Command, new(entry.Command, CommandType.Undo, entry.Storables));

            _logger.LogInformation("Command '{CommandType}' undone.", entry.Command.GetType());
        }
    }

    public void Redo()
    {
        _logger.LogDebug("Attempting to redo last undone command.");

        if (_redoStack.Count >= 1)
        {
            if (!SemaphoreWait())
            {
                return;
            }

            Entry? entry = _redoStack.Pop();
            if (entry == null)
            {
                _logger.LogWarning("Redo stack is empty.");
                _semaphoreSlim.Release();
                return;
            }

            CanRedo = _redoStack.Count > 0;

            try
            {
                _executingCommand = entry.Command;
                entry.Command.Redo();

                _undoStack.Push(entry);
                CanUndo = _undoStack.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while redoing command '{CommandType}'.", entry.Command.GetType());
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
            finally
            {
                _semaphoreSlim.Release();
                _executingCommand = null;
            }

            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(entry.Command, new(entry.Command, CommandType.Redo, entry.Storables));

            _logger.LogInformation("Command '{CommandType}' redone.", entry.Command.GetType());
        }
    }

    public void Clear()
    {
        _logger.LogDebug("Clearing all command stacks.");

        LastExecutedTime = DateTime.MinValue;
        _undoStack.Clear();
        _redoStack.Clear();
        CanUndo = false;
        CanRedo = false;

        Cleared?.Invoke(this, EventArgs.Empty);

        _logger.LogInformation("All command stacks cleared.");
    }
}
