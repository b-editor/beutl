using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Properties;

namespace BEditor.Core.Command
{
    public sealed class RecordCommand<T> : IRecordCommand
    {
        private readonly Action<T> _do;
        private readonly Action<T> _redo;
        private readonly Action<T> _undo;
        private readonly Func<T, string>? _getName;
        private readonly T value;

        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Func<T, string>? getName = null)
        {
            value = args;
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
            _getName = getName;
        }
        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Action<T> onRedo, Func<T, string>? getName = null)
        {
            value = args;
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        public string Name => _getName?.Invoke(value) ?? Resources.UnknownCommand;

        public void Do() => _do?.Invoke(value);
        public void Redo() => _redo?.Invoke(value);
        public void Undo() => _undo?.Invoke(value);
    }
    public sealed class RecordCommand : IRecordCommand
    {
        private readonly Action _do;
        private readonly Action _redo;
        private readonly Action _undo;
        private readonly Func<string>? _getName;

        public RecordCommand(Action onDo, Action onUndo, Func<string>? getName = null)
        {
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
            _getName = getName;
        }
        public RecordCommand(Action onDo, Action onUndo, Action onRedo, Func<string>? getName = null)
        {
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
            _getName = getName;
        }

        public string Name => _getName?.Invoke() ?? Resources.UnknownCommand;

        public static RecordCommand<T> Create<T>(T args, Action<T> onDo, Action<T> onUndo, Func<T, string>? getName = null)
        {
            return new RecordCommand<T>(args, onDo, onUndo, getName);
        }
        public static RecordCommand<T> Create<T>(T args, Action<T> onDo, Action<T> onUndo, Action<T> onRedo, Func<T, string>? getName = null)
        {
            return new RecordCommand<T>(args, onDo, onUndo, onRedo, getName);
        }
        public void Do() => _do?.Invoke();
        public void Redo() => _redo?.Invoke();
        public void Undo() => _undo?.Invoke();
    }
}
