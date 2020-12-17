using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Drawing;

namespace BEditor.Core.Command
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

                    CanUndoChange?.Invoke(null, EventArgs.Empty);
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

                    CanRedoChange?.Invoke(null, EventArgs.Empty);
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
        public static event EventHandler CanUndoChange;

        /// <summary>
        /// Redo出来るかどうかの状態が変化すると発生します
        /// </summary>
        public static event EventHandler CanRedoChange;

        /// <summary>
        /// UnDo ReDo Do時に発生します
        /// </summary>
        public static event EventHandler<CommandType> Executed;

        /// <summary>
        /// コマンドがキャンセルされたときに発生します
        /// </summary>
        public static event EventHandler CommandCancel;
        /// <summary>
        /// <see cref="Clear"/> 後に発生します
        /// </summary>
        public static event EventHandler CommandsClear;

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

            CanUndo = UndoStack.Count > 0;

            try
            {
                process = false;
                command.Do();

                UndoStack.Push(command);

                RedoStack.Clear();
                CanRedo = RedoStack.Count > 0;
            }
            catch
            {
                CommandCancel?.Invoke(null, EventArgs.Empty);
            }

            process = true;
            Executed?.Invoke(command, CommandType.Do);
        }
        /// <summary>
        /// 操作を実行し、かつその内容をStackに追加します
        /// </summary>
        /// <param name="command">実行するコマンド</param>
        public static Task DoAsyns(IRecordCommand command)
        {
            return Task.Run(() =>
            {
                if (!process)
                {
                    Debug.WriteLine("if (!process) {...");
                    return;
                }

                CanUndo = UndoStack.Count > 0;

                try
                {
                    process = false;
                    command.Do();

                    UndoStack.Push(command);

                    RedoStack.Clear();
                    CanRedo = RedoStack.Count > 0;
                }
                catch
                {
                    CommandCancel?.Invoke(null, EventArgs.Empty);
                }

                process = true;
                Executed?.Invoke(command, CommandType.Do);
            });
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

                Executed?.Invoke(command, CommandType.Undo);
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
                Executed?.Invoke(command, CommandType.Redo);
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

            CommandsClear?.Invoke(null, EventArgs.Empty);
        }

        #endregion
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
