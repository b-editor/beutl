// RecordCommand{TState}.cs
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
    /// <typeparam name="TState">The type of state.</typeparam>
    public sealed class RecordCommand<TState> : IRecordCommand
    {
        private readonly Action<TState> _do;
        private readonly Action<TState> _redo;
        private readonly Action<TState> _undo;
        private readonly Func<TState, string>? _getName;
        private readonly TState _state;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="onDo">Execute the operation.</param>
        /// <param name="onUndo">Undo the operation.</param>
        /// <param name="getName">Gets the name of the command.</param>
        public RecordCommand(TState state, Action<TState> onDo, Action<TState> onUndo, Func<TState, string>? getName = null)
        {
            _state = state;
            _do = onDo ?? throw new ArgumentNullException(nameof(onDo));
            _redo = onDo;
            _undo = onUndo ?? throw new ArgumentNullException(nameof(onUndo));
            _getName = getName ?? throw new ArgumentNullException(nameof(getName));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="onDo">Execute the operation.</param>
        /// <param name="onUndo">Undo the operation.</param>
        /// <param name="onRedo">Redo the operation.</param>
        /// <param name="getName">Gets the name of the command.</param>
        public RecordCommand(TState state, Action<TState> onDo, Action<TState> onUndo, Action<TState> onRedo, Func<TState, string>? getName = null)
        {
            _state = state;
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        /// <inheritdoc/>
        public string Name => _getName?.Invoke(_state) ?? Strings.UnknownCommand;

        /// <inheritdoc/>
        public void Do() => _do?.Invoke(_state);

        /// <inheritdoc/>
        public void Redo() => _redo?.Invoke(_state);

        /// <inheritdoc/>
        public void Undo() => _undo?.Invoke(_state);
    }
}