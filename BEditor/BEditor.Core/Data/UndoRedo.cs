using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.PropertyData;

namespace BEditor.Core.Data
{
    /// <summary>
    /// 実行、元に戻す(undo)、やり直す(redo)の動作を表します
    /// </summary>
    public interface IUndoRedoCommand
    {
        /// <summary>
        /// 操作を実行します
        /// <para>例外を投げた場合キャンセルされます</para>
        /// </summary>
        public void Do();

        /// <summary>
        /// 操作を元に戻します
        /// </summary>
        public void Undo();

        /// <summary>
        /// 操作をやり直します
        /// </summary>
        public void Redo();
    }

    /// <summary>
    /// 行なった操作の履歴を蓄積することでUndo,Redoの機能を表します
    /// </summary>
    public static class UndoRedoManager
    {
        /// <summary>
        /// コマンドに対応する名前が入った配列を取得します
        /// </summary>
        public static Dictionary<Type, string> CommandTypeDictionary { get; } = new Dictionary<Type, string>() {
            #region キーフレーム操作
            { typeof(EaseProperty.AddCommand),    "キーフレームを追加" },
            { typeof(EaseProperty.RemoveCommand), "キーフレームを削除" },
            { typeof(EaseProperty.MoveCommand),   "キーフレームを移動" },
            { typeof(ColorAnimationProperty.AddCommand),    "キーフレームを追加" },
            { typeof(ColorAnimationProperty.RemoveCommand), "キーフレームを削除" },
            { typeof(ColorAnimationProperty.MoveCommand),   "キーフレームを移動" },
            #endregion

            #region タイムライン操作
            { typeof(ClipData.AddCommand),          "オブジェクトを追加" },
            { typeof(ClipData.RemoveCommand),       "オブジェクトを削除" },
            { typeof(ClipData.MoveCommand),         "オブジェクトを移動" },
            { typeof(ClipData.LengthChangeCommand), "オブジェクトの長さ変更" },
            //{ typeof(ClipData.PasteClip),        "オブジェクトをペースト" },
            //{ typeof(ClipData.CopyClip),         "オブジェクトをコピー" },
            //{ typeof(ClipData.CutClip),          "オブジェクトをカット" },
            #endregion

            #region エフェクト操作
            { typeof(EffectElement.CheckCommand),  "エフェクトを無効化" },
            { typeof(EffectElement.UpCommand),     "エフェクトの階層 上" },
            { typeof(EffectElement.DownCommand),   "エフェクトの階層 下" },
            { typeof(EffectElement.RemoveCommand), "エフェクトを削除" },
            { typeof(EffectElement.AddCommand), "エフェクトを追加" },
            #endregion

            #region プロパティ変更
            { typeof(EaseProperty.ChangeValueCommand), "値の変更" },
            { typeof(EaseProperty.ChangeEaseCommand), "イージングの変更" },

            { typeof(ColorAnimationProperty.ChangeEaseCommand), "イージングの変更" },
            { typeof(ColorAnimationProperty.ChangeColorCommand), "色の変更" },


            { typeof(ColorProperty.ChangeColorCommand), "色の変更" },

            { typeof(DocumentProperty.TextChangeCommand), "ドキュメントのテキストを変更" },

            { typeof(SelectorProperty.ChangeSelectCommand), "コンボボックス変更" },
            { typeof(FontProperty.ChangeSelectCommand), "フォントの変更" },


            { typeof(CheckProperty.ChangeCheckedCommand), "チェックボックス変更" },


            { typeof(FileProperty.ChangeFileCommand), "ファイル変更" }
            #endregion
        };

        internal static bool process = true;
        /// <summary>
        /// 実行後またはRedo後に記録
        /// </summary>
        public static Stack<IUndoRedoCommand> UndoStack { get; } = new Stack<IUndoRedoCommand>();
        /// <summary>
        /// Undo後に記録
        /// </summary>
        public static Stack<IUndoRedoCommand> RedoStack { get; } = new Stack<IUndoRedoCommand>();
        private static bool canUndo = false;
        private static bool canRedo = false;

        /// <summary>
        /// 操作を実行し、かつその内容をStackに追加します
        /// </summary>
        /// <param name="command">実行するコマンド</param>
        public static void Do(IUndoRedoCommand command)
        {
            if (!process) return;

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
            Component.Current.Status = Status.Edit;
            DidEvent?.Invoke(command, CommandType.Do);
        }

        /// <summary>
        /// 行なったコマンドを取り消してひとつ前の状態に戻します
        /// </summary>
        public static void Undo()
        {
            if (!process) return;

            if (UndoStack.Count >= 1)
            {
                IUndoRedoCommand command = UndoStack.Pop();
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

                DidEvent?.Invoke(command, CommandType.Undo);
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
                IUndoRedoCommand command = RedoStack.Pop();
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
                DidEvent?.Invoke(command, CommandType.Redo);
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
        public static event EventHandler<CommandType> DidEvent;

        /// <summary>
        /// コマンドがキャンセルされたときに発生します
        /// </summary>
        public static event EventHandler CommandCancel;
        /// <summary>
        /// <see cref="Clear"/> 後に発生します
        /// </summary>
        public static event EventHandler CommandsClear;
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
