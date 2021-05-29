// CommandManager.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

using BEditor.Data;

namespace BEditor.Command
{
    /// <summary>
    /// Indicates the type of command.
    /// </summary>
    public enum CommandType
    {
        /// <summary>
        /// Do.
        /// </summary>
        Do,

        /// <summary>
        /// Undo.
        /// </summary>
        Undo,

        /// <summary>
        /// Redo.
        /// </summary>
        Redo,
    }

    /// <summary>
    /// Indicates the Undo and Redo functions by storing the history of the operations performed.
    /// </summary>
    public class CommandManager : BasePropertyChanged
    {
        /// <summary>
        /// Default instance of CommandManager.
        /// </summary>
        public static readonly CommandManager Default = new();
        private static readonly PropertyChangedEventArgs _canUndoArgs = new(nameof(CanUndo));
        private static readonly PropertyChangedEventArgs _canRedoArgs = new(nameof(CanRedo));
        private bool _process = true;
        private bool _canUndo = false;
        private bool _canRedo = false;

        /// <summary>
        /// Occurs at UnDo, Redo, and execution time.
        /// </summary>
        public event EventHandler<CommandType>? Executed;

        /// <summary>
        /// Occurs when a command is canceled.
        /// </summary>
        public event EventHandler? CommandCancel;

        /// <summary>
        /// Occurs after <see cref="Clear"/>.
        /// </summary>
        public event EventHandler? CommandsClear;

        /// <summary>
        /// Gets the <see cref="Stack{T}"/> that will be recorded after execution or redo.
        /// </summary>
        public Stack<IRecordCommand> UndoStack { get; } = new();

        /// <summary>
        /// Gets the <see cref="Stack{T}"/> to be recorded after the Undo.
        /// </summary>
        public Stack<IRecordCommand> RedoStack { get; } = new();

        /// <summary>
        /// Gets a value indicating whether or not Undo is enabled.
        /// </summary>
        public bool CanUndo
        {
            get => _canUndo;
            private set => SetValue(value, ref _canUndo, _canUndoArgs);
        }

        /// <summary>
        /// Gets a value indicating whether or not Redo is enabled.
        /// </summary>
        public bool CanRedo
        {
            get => _canRedo;
            private set => SetValue(value, ref _canRedo, _canRedoArgs);
        }

        /// <summary>
        /// Execute the operation and add its contents to the stack.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public void Do(IRecordCommand command)
        {
            if (!_process)
            {
                Debug.WriteLine("if (!process) {...");
                return;
            }

            try
            {
                _process = false;
                command.Do();

                UndoStack.Push(command);
                CanUndo = UndoStack.Count > 0;

                RedoStack.Clear();
                CanRedo = RedoStack.Count > 0;
            }
            catch
            {
                Debug.Fail("Commandの実行中に例外が発生。");
                CommandCancel?.Invoke(null, EventArgs.Empty);
            }

            _process = true;
            Executed?.Invoke(command, CommandType.Do);
        }

        /// <summary>
        /// Undoes a command and returns to the previous state.
        /// </summary>
        public void Undo()
        {
            if (!_process) return;

            if (UndoStack.Count >= 1)
            {
                var command = UndoStack.Pop();
                CanUndo = UndoStack.Count > 0;

                try
                {
                    _process = false;
                    command.Undo();

                    RedoStack.Push(command);
                    CanRedo = RedoStack.Count > 0;
                }
                catch
                {
                }

                _process = true;

                Executed?.Invoke(command, CommandType.Undo);
            }
        }

        /// <summary>
        /// Redo the canceled command.
        /// </summary>
        public void Redo()
        {
            if (!_process) return;

            if (RedoStack.Count >= 1)
            {
                var command = RedoStack.Pop();
                CanRedo = RedoStack.Count > 0;

                try
                {
                    _process = false;
                    command.Redo();

                    UndoStack.Push(command);
                    CanUndo = UndoStack.Count > 0;
                }
                catch
                {
                }

                _process = true;
                Executed?.Invoke(command, CommandType.Redo);
            }
        }

        /// <summary>
        /// Clear the recorded commands.
        /// </summary>
        public void Clear()
        {
            UndoStack.Clear();
            RedoStack.Clear();
            CanUndo = false;
            CanRedo = false;

            CommandsClear?.Invoke(this, EventArgs.Empty);
        }
    }
}