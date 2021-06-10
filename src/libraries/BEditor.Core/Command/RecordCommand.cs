// RecordCommand.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Resources;

namespace BEditor.Command
{
    /// <summary>
    /// Represents the action of executing, undoing, or redoing.
    /// </summary>
    public sealed class RecordCommand : IRecordCommand
    {
        private readonly Action _do;
        private readonly Action _redo;
        private readonly Action _undo;
        private readonly Func<string>? _getName;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand"/> class.
        /// </summary>
        /// <param name="onDo">Execute the operation.</param>
        /// <param name="onUndo">Undo the operation.</param>
        /// <param name="getName">Gets the name of the command.</param>
        public RecordCommand(Action onDo, Action onUndo, Func<string>? getName = null)
        {
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
            _getName = getName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand"/> class.
        /// </summary>
        /// <param name="onDo">Execute the operation.</param>
        /// <param name="onUndo">Undo the operation.</param>
        /// <param name="onRedo">Redo the operation.</param>
        /// <param name="getName">Gets the name of the command.</param>
        public RecordCommand(Action onDo, Action onUndo, Action onRedo, Func<string>? getName = null)
        {
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        /// <summary>
        /// Gets the empty record command.
        /// </summary>
        public static IRecordCommand Empty { get; } = new EmptyCommand();

        /// <inheritdoc/>
        public string Name => _getName?.Invoke() ?? Strings.UnknownCommand;

        /// <summary>
        /// Create a RecordCommand.
        /// </summary>
        /// <typeparam name="TState">The type of state.</typeparam>
        /// <param name="state">The state.</param>
        /// <param name="onDo">Execute the operation.</param>
        /// <param name="onUndo">Undo the operation.</param>
        /// <param name="getName">Gets the name of the command.</param>
        /// <returns>An instance of <see cref="RecordCommand{T}"/> created.</returns>
        public static RecordCommand<TState> Create<TState>(TState state, Action<TState> onDo, Action<TState> onUndo, Func<TState, string>? getName = null)
        {
            return new RecordCommand<TState>(state, onDo, onUndo, getName);
        }

        /// <summary>
        /// Create a RecordCommand.
        /// </summary>
        /// <typeparam name="TState">The type of state.</typeparam>
        /// <param name="state">The state.</param>
        /// <param name="onDo">Execute the operation.</param>
        /// <param name="onUndo">Undo the operation.</param>
        /// <param name="onRedo">Redo the operation.</param>
        /// <param name="getName">Gets the name of the command.</param>
        /// <returns>An instance of <see cref="RecordCommand{T}"/> created.</returns>
        public static RecordCommand<TState> Create<TState>(TState state, Action<TState> onDo, Action<TState> onUndo, Action<TState> onRedo, Func<TState, string>? getName = null)
        {
            return new RecordCommand<TState>(state, onDo, onUndo, onRedo, getName);
        }

        /// <inheritdoc/>
        public void Do() => _do?.Invoke();

        /// <inheritdoc/>
        public void Redo() => _redo?.Invoke();

        /// <inheritdoc/>
        public void Undo() => _undo?.Invoke();
    }
}