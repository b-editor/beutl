using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Command
{
    public sealed class RecordCommand<T> : IRecordCommand
    {
        private readonly Action<T> _do;
        private readonly Action<T> _redo;
        private readonly Action<T> _undo;
        private readonly T value;

        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo)
        {
            value = args;
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
        }
        public RecordCommand(T args, Action<T> onDo, Action<T> onUndo, Action<T> onRedo)
        {
            value = args;
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
        }

        public void Do() => _do?.Invoke(value);
        public void Redo() => _redo?.Invoke(value);
        public void Undo() => _undo?.Invoke(value);
    }
    public sealed class RecordCommand : IRecordCommand
    {
        private readonly Action _do;
        private readonly Action _redo;
        private readonly Action _undo;

        public RecordCommand(Action onDo, Action onUndo)
        {
            _do = onDo;
            _redo = onDo;
            _undo = onUndo;
        }
        public RecordCommand(Action onDo, Action onUndo, Action onRedo)
        {
            _do = onDo;
            _redo = onRedo;
            _undo = onUndo;
        }

        public void Do() => _do?.Invoke();
        public void Redo() => _redo?.Invoke();
        public void Undo() => _undo?.Invoke();
    }
}
