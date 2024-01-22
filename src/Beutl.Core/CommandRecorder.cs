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
            _logger.LogInformation("IRecordableCommand.Nothing is True. (Type: {Type})", command.GetType());
            return null;
        }

        Entry entry = CreateEntry(command);
        if (entry.Storables.Count == 0)
        {
            _logger.LogWarning("Storables.Count is 0. (Type: {Type})", command.GetType());
        }

        return entry;
    }

    private bool SemaphoreWait()
    {
        if (!_semaphoreSlim.Wait(1000))
        {
            _logger.LogWarning("SemaphoreSlim timeout. (ExecutingCommand: {Command})", _executingCommand);
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
    }

    public void DoAndPush(IRecordableCommand command)
    {
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
            _logger.LogError(ex, "An exception occurred while executing the command. {Command}", command);
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
    }

    public void Undo()
    {
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
                _logger.LogError(ex, "An exception occurred while executing the undo command. {Command}", entry.Command);
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
            finally
            {
                _semaphoreSlim.Release();
                _executingCommand = null;
            }

            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(entry.Command, new(entry.Command, CommandType.Undo, entry.Storables));
        }
    }

    public void Redo()
    {
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
                _logger.LogError(ex, "An exception occurred while executing the redo command. {Command}", entry.Command);
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
            finally
            {
                _semaphoreSlim.Release();
                _executingCommand = null;
            }

            LastExecutedTime = DateTime.UtcNow;
            Executed?.Invoke(entry.Command, new(entry.Command, CommandType.Redo, entry.Storables));
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
