using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Properties;

namespace BEditor.Core.Command
{
    /// <summary>
    /// Represents the action of executing, undoing, or redoing.
    /// </summary>
    /// <typeparam name="T">Argument type</typeparam>
    public sealed class RecordCommand<T> : IRecordCommand
    {
        private readonly Action<T> _do;
        private readonly Action<T> _redo;
        private readonly Action<T> _undo;
        private readonly Func<T, string>? _getName;
        private readonly T value;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Func<T, string>? getName = null)
        {
            value = args;
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
            _getName = getName;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Action<T> onRedo, Func<T, string>? getName = null)
        {
            value = args;
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        /// <inheritdoc/>
        public string Name => _getName?.Invoke(value) ?? Resources.UnknownCommand;

        /// <inheritdoc/>
        public void Do() => _do?.Invoke(value);
        /// <inheritdoc/>
        public void Redo() => _redo?.Invoke(value);
        /// <inheritdoc/>
        public void Undo() => _undo?.Invoke(value);
    }
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
        public RecordCommand(Action onDo, Action onUndo, Func<string>? getName = null)
        {
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
            _getName = getName;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="RecordCommand{T}"/> class.
        /// </summary>
        public RecordCommand(Action onDo, Action onUndo, Action onRedo, Func<string>? getName = null)
        {
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        /// <inheritdoc/>
        public string Name => _getName?.Invoke() ?? Resources.UnknownCommand;

        /// <summary>
        /// Create a RecordCommand
        /// </summary>
        /// <typeparam name="T">Argument type</typeparam>
        public static RecordCommand<T> Create<T>(T args, Action<T> onDo, Action<T> onUndo, Func<T, string>? getName = null)
        {
            return new RecordCommand<T>(args, onDo, onUndo, getName);
        }
        /// <summary>
        /// Create a RecordCommand
        /// </summary>
        /// <typeparam name="T">Argument type</typeparam>
        public static RecordCommand<T> Create<T>(T args, Action<T> onDo, Action<T> onUndo, Action<T> onRedo, Func<T, string>? getName = null)
        {
            return new RecordCommand<T>(args, onDo, onUndo, onRedo, getName);
        }
        /// <inheritdoc/>
        public void Do() => _do?.Invoke();
        /// <inheritdoc/>
        public void Redo() => _redo?.Invoke();
        /// <inheritdoc/>
        public void Undo() => _undo?.Invoke();
    }
}
