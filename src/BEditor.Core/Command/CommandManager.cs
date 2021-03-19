using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

using BEditor.Data;

namespace BEditor.Command
{
    /// <summary>
    /// 行なった操作の履歴を蓄積することでUndo,Redoの機能を表します
    /// </summary>
    public static class CommandManager
    {
        #region Fields

        internal static bool process = true;
        private static bool canUndo = false;
        private static bool canRedo = false;

        #endregion

        #region Properties

        /// <summary>
        /// 実行後またはRedo後に記録
        /// </summary>
        public static Stack<IRecordCommand> UndoStack { get; } = new Stack<IRecordCommand>();
        /// <summary>
        /// Undo後に記録
        /// </summary>
        public static Stack<IRecordCommand> RedoStack { get; } = new Stack<IRecordCommand>();
        /// <summary>
        /// Undo出来るか取得します
        /// </summary>
        public static bool CanUndo
        {
            private set
            {
                if (canUndo != value)
                {
                    canUndo = value;

                    CanUndoChange(null, EventArgs.Empty);
                }
            }
            get
            {
                return canUndo;
            }
        }
        /// <summary>
        /// Redo出来るかを取得します
        /// </summary>
        public static bool CanRedo
        {
            private set
            {
                if (canRedo != value)
                {
                    canRedo = value;

                    CanRedoChange(null, EventArgs.Empty);
                }
            }
            get
            {
                return canRedo;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Undo出来るかどうかの状態が変化すると発生します
        /// </summary>
        public static event EventHandler CanUndoChange = delegate { };

        /// <summary>
        /// Redo出来るかどうかの状態が変化すると発生します
        /// </summary>
        public static event EventHandler CanRedoChange = delegate { };

        /// <summary>
        /// UnDo ReDo Do時に発生します
        /// </summary>
        public static event EventHandler<CommandType> Executed = delegate { };

        /// <summary>
        /// コマンドがキャンセルされたときに発生します
        /// </summary>
        public static event EventHandler CommandCancel = delegate { };
        /// <summary>
        /// <see cref="Clear"/> 後に発生します
        /// </summary>
        public static event EventHandler CommandsClear = delegate { };

        #endregion

        #region Methods

        /// <summary>
        /// 操作を実行し、かつその内容をStackに追加します
        /// </summary>
        /// <param name="command">実行するコマンド</param>
        public static void Do(IRecordCommand command)
        {
            if (!process)
            {
                Debug.WriteLine("if (!process) {...");
                return;
            }

            try
            {
                process = false;
                command.Do();

                UndoStack.Push(command);
                CanUndo = UndoStack.Count > 0;

                RedoStack.Clear();
                CanRedo = RedoStack.Count > 0;
            }
            catch
            {
                Debug.Assert(false);
                CommandCancel(null, EventArgs.Empty);
            }

            process = true;
            Executed(command, CommandType.Do);
        }
        /// <summary>
        /// 行なったコマンドを取り消してひとつ前の状態に戻します
        /// </summary>
        public static void Undo()
        {
            if (!process) return;

            if (UndoStack.Count >= 1)
            {
                IRecordCommand command = UndoStack.Pop();
                CanUndo = UndoStack.Count > 0;

                try
                {
                    process = false;
                    command.Undo();

                    RedoStack.Push(command);
                    CanRedo = RedoStack.Count > 0;
                }
                catch { }
                process = true;

                Executed(command, CommandType.Undo);
            }
        }
        /// <summary>
        /// 取り消したコマンドをやり直します
        /// </summary>
        public static void Redo()
        {
            if (!process) return;

            if (RedoStack.Count >= 1)
            {
                IRecordCommand command = RedoStack.Pop();
                CanRedo = RedoStack.Count > 0;

                try
                {
                    process = false;
                    command.Redo();

                    UndoStack.Push(command);
                    CanUndo = UndoStack.Count > 0;
                }
                catch { }

                process = true;
                Executed(command, CommandType.Redo);
            }
        }
        /// <summary>
        /// 記録されたコマンドを初期化
        /// </summary>
        public static void Clear()
        {
            UndoStack.Clear();
            RedoStack.Clear();
            CanUndo = false;
            CanRedo = false;

            CommandsClear(null, EventArgs.Empty);
        }

        #endregion
    }

    /// <summary>
    /// 行なった操作の履歴を蓄積することでUndo,Redoの機能を表します
    /// </summary>
    public class NewCommandManager : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs _canUndoArgs = new(nameof(CanUndo));
        private static readonly PropertyChangedEventArgs _canRedoArgs = new(nameof(CanRedo));
        private bool _process = true;
        private bool _canUndo = false;
        private bool _canRedo = false;

        /// <summary>
        /// 実行後またはRedo後に記録
        /// </summary>
        public Stack<IRecordCommand> UndoStack { get; } = new();
        /// <summary>
        /// Undo後に記録
        /// </summary>
        public Stack<IRecordCommand> RedoStack { get; } = new();
        /// <summary>
        /// Undo出来るか取得します
        /// </summary>
        public bool CanUndo
        {
            get => _canUndo;
            set => SetValue(value, ref _canUndo, _canUndoArgs);
        }
        /// <summary>
        /// Redo出来るかを取得します
        /// </summary>
        public bool CanRedo
        {
            get => _canRedo;
            set => SetValue(value, ref _canRedo, _canRedoArgs);
        }

        /// <summary>
        /// UnDo ReDo Do時に発生します
        /// </summary>
        public static event EventHandler<CommandType> Executed = delegate { };
        /// <summary>
        /// コマンドがキャンセルされたときに発生します
        /// </summary>
        public static event EventHandler CommandCancel = delegate { };
        /// <summary>
        /// <see cref="Clear"/> 後に発生します
        /// </summary>
        public static event EventHandler CommandsClear = delegate { };

        /// <summary>
        /// 操作を実行し、かつその内容をStackに追加します
        /// </summary>
        /// <param name="command">実行するコマンド</param>
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
                Debug.Assert(false);
                CommandCancel(null, EventArgs.Empty);
            }

            _process = true;
            Executed(command, CommandType.Do);
        }
        /// <summary>
        /// 行なったコマンドを取り消してひとつ前の状態に戻します
        /// </summary>
        public void Undo()
        {
            if (!_process) return;

            if (UndoStack.Count >= 1)
            {
                IRecordCommand command = UndoStack.Pop();
                CanUndo = UndoStack.Count > 0;

                try
                {
                    _process = false;
                    command.Undo();

                    RedoStack.Push(command);
                    CanRedo = RedoStack.Count > 0;
                }
                catch { }
                _process = true;

                Executed(command, CommandType.Undo);
            }
        }
        /// <summary>
        /// 取り消したコマンドをやり直します
        /// </summary>
        public void Redo()
        {
            if (!_process) return;

            if (RedoStack.Count >= 1)
            {
                IRecordCommand command = RedoStack.Pop();
                CanRedo = RedoStack.Count > 0;

                try
                {
                    _process = false;
                    command.Redo();

                    UndoStack.Push(command);
                    CanUndo = UndoStack.Count > 0;
                }
                catch { }

                _process = true;
                Executed(command, CommandType.Redo);
            }
        }
        /// <summary>
        /// 記録されたコマンドを初期化
        /// </summary>
        public void Clear()
        {
            UndoStack.Clear();
            RedoStack.Clear();
            CanUndo = false;
            CanRedo = false;

            CommandsClear(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// コマンドの種類を表します
    /// </summary>
    public enum CommandType
    {
        /// <summary>
        /// 
        /// </summary>
        Do,
        /// <summary>
        /// 
        /// </summary>
        Undo,
        /// <summary>
        /// 
        /// </summary>
        Redo
    }
}
