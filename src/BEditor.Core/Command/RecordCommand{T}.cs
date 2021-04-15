using System;

using BEditor.Resources;

namespace BEditor.Command
{
    /// <summary>
    /// Represents the action of executing, undoing, or redoing.
    /// </summary>
    /// <typeparam name="T">Argument type.</typeparam>
    public sealed class RecordCommand<T> : IRecordCommand
    {
        private readonly Action<T> _do;
        private readonly Action<T> _redo;
        private readonly Action<T> _undo;
        private readonly Func<T, string>? _getName;
        private readonly T _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Func<T, string>? getName = null)
        {
            _value = args;
            _do = onDo ?? throw new ArgumentNullException(nameof(onDo));
            _redo = onDo;
            _undo = onUndo ?? throw new ArgumentNullException(nameof(onUndo));
            _getName = getName ?? throw new ArgumentNullException(nameof(getName));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Action<T> onRedo, Func<T, string>? getName = null)
        {
            _value = args;
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        /// <inheritdoc/>
        public string Name => _getName?.Invoke(_value) ?? Strings.UnknownCommand;

        /// <inheritdoc/>
        public void Do() => _do?.Invoke(_value);

        /// <inheritdoc/>
        public void Redo() => _redo?.Invoke(_value);

        /// <inheritdoc/>
        public void Undo() => _undo?.Invoke(_value);
    }
}