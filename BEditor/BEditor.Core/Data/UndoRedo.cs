using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.PropertyData;

namespace BEditor.Core.Data {
    /// <summary>
    /// 実行、元に戻す(undo)、やり直す(redo)の各動作を定義するインターフェース
    /// </summary>
    public interface IUndoRedoCommand {
        /// <summary>
        /// 操作を実行するメソッド
        /// <para>例外を投げた場合キャンセルされます</para>
        /// </summary>
        public void Do();

        /// <summary>
        /// 操作を元に戻すメソッド
        /// </summary>
        public void Undo();

        /// <summary>
        /// 操作をやり直すメソッド
        /// </summary>
        public void Redo();
    }

    /// <summary>
    /// 行なった操作の履歴を蓄積することでUndo,Redoの機能を提供するクラス
    /// </summary>
    public static class UndoRedoManager {
        
        public static readonly Dictionary<Type, string> CommandTypeDictionary = new Dictionary<Type, string>() {
            #region キーフレーム操作
            { typeof(EaseProperty.Add),    "キーフレームを追加" },
            { typeof(EaseProperty.Remove), "キーフレームを削除" },
            { typeof(EaseProperty.Move),   "キーフレームを移動" },
            { typeof(ColorAnimationProperty.Add),    "キーフレームを追加" },
            { typeof(ColorAnimationProperty.Remove), "キーフレームを削除" },
            { typeof(ColorAnimationProperty.Move),   "キーフレームを移動" },
            #endregion

            #region タイムライン操作
            { typeof(ClipData.Add),          "オブジェクトを追加" },
            { typeof(ClipData.Remove),       "オブジェクトを削除" },
            { typeof(ClipData.Move),         "オブジェクトを移動" },
            { typeof(ClipData.LengthChange), "オブジェクトの長さ変更" },
            //{ typeof(ClipData.PasteClip),        "オブジェクトをペースト" },
            //{ typeof(ClipData.CopyClip),         "オブジェクトをコピー" },
            //{ typeof(ClipData.CutClip),          "オブジェクトをカット" },
            #endregion

            #region エフェクト操作
            { typeof(EffectElement.CheckEffect),  "エフェクトを無効化" },
            { typeof(EffectElement.UpEffect),     "エフェクトの階層 上" },
            { typeof(EffectElement.DownEffect),   "エフェクトの階層 下" },
            { typeof(EffectElement.DeleteEffect), "エフェクトを削除" },
            { typeof(EffectElement.AddEffect), "エフェクトを追加" },
            #endregion

            #region プロパティ変更
            { typeof(EaseProperty.ChangeValue), "値の変更" },
            { typeof(EaseProperty.ChangeEase), "イージングの変更" },

            { typeof(ColorAnimationProperty.ChangeEase), "イージングの変更" },
            { typeof(ColorAnimationProperty.ChangeColor), "色の変更" },


            { typeof(ColorProperty.ChangeColor), "色の変更" },

            { typeof(DocumentProperty.TextChangedCommand), "ドキュメントのテキストを変更" },

            { typeof(SelectorProperty.ChangeSelect), "コンボボックス変更" },
            { typeof(FontProperty.ChangeSelect), "フォントの変更" },


            { typeof(CheckProperty.ChangeChecked), "チェックボックス変更" },


            { typeof(FileProperty.ChangePath), "ファイル変更" }
            #endregion
        };

        /// <summary>
        /// 実行中の場合False
        /// </summary>
        internal static bool process = true;
        public static Stack<IUndoRedoCommand> UndoStack = new Stack<IUndoRedoCommand>();
        public static Stack<IUndoRedoCommand> RedoStack = new Stack<IUndoRedoCommand>();
        private static bool canUndo = false;
        private static bool canRedo = false;

        /// <summary>
        /// 操作を実行し、かつその内容をStackに追加
        /// </summary>
        /// <param name="command">IUndoRedoCommandインターフェースを実装するオブジェクト</param>
        public static void Do(IUndoRedoCommand command) {
            if (!process) return;

            CanUndo = UndoStack.Count > 0;

            try {
                process = false;
                command.Do();
                process = true;

                UndoStack.Push(command);

                RedoStack.Clear();
                CanRedo = RedoStack.Count > 0;
            }
            catch {
                CommandCancel?.Invoke(null, EventArgs.Empty);
            }

            Component.Current.Status = Status.Edit;
            DidEvent?.Invoke(command, CommandType.Do);
        }

        /// <summary>
        /// 行なったコマンドを取り消してひとつ前の状態に戻す
        /// </summary>
        public static void Undo() {
            if (!process) return;

            if (UndoStack.Count >= 1) {
                IUndoRedoCommand command = UndoStack.Pop();
                CanUndo = UndoStack.Count > 0;

                try {
                    process = false;
                    command.Undo();
                    process = true;

                    RedoStack.Push(command);
                    CanRedo = RedoStack.Count > 0;
                }
                catch { }

                DidEvent?.Invoke(command, CommandType.Undo);
            }
        }

        /// <summary>
        /// 取り消したコマンドをやり直す
        /// </summary>
        public static void Redo() {
            if (!process) return;

            if (RedoStack.Count >= 1) {
                IUndoRedoCommand command = RedoStack.Pop();
                CanRedo = RedoStack.Count > 0;

                try {
                    process = false;
                    command.Redo();
                    process = true;

                    UndoStack.Push(command);
                    CanUndo = UndoStack.Count > 0;
                }
                catch { }

                DidEvent?.Invoke(command, CommandType.Redo);
            }
        }

        /// <summary>
        /// コマンドをリセット
        /// </summary>
        public static void Clear() {
            UndoStack.Clear();
            RedoStack.Clear();
            CanUndo = false;
            CanRedo = false;

            CommandsClear?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Undo出来るかどうかを返す
        /// </summary>
        public static bool CanUndo {
            private set {
                if (canUndo != value) {
                    canUndo = value;

                    CanUndoChange?.Invoke(null, EventArgs.Empty);
                }
            }
            get {
                return canUndo;
            }
        }

        /// <summary>
        /// Redo出来るかどうかを返す
        /// </summary>
        public static bool CanRedo {
            private set {
                if (canRedo != value) {
                    canRedo = value;

                    CanRedoChange?.Invoke(null, EventArgs.Empty);
                }
            }
            get {
                return canRedo;
            }
        }

        /// <summary>
        /// Undo出来るかどうかの状態が変化すると発生
        /// </summary>
        public static event EventHandler CanUndoChange;

        /// <summary>
        /// Redo出来るかどうかの状態が変化すると発生
        /// </summary>
        public static event EventHandler CanRedoChange;

        /// <summary>
        /// UnDo ReDo Do時に発生
        /// </summary>
        public static event EventHandler<CommandType> DidEvent;

        public static event EventHandler CommandCancel;
        public static event EventHandler CommandsClear;
    }

    public enum CommandType {
        Do,
        Undo,
        Redo
    }
}
