using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data;
using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;

namespace BEditor.Core.Extensions {
    public static class ExtensionCommand {
        /// <summary>
        /// <see cref="EffectElement.CheckCommand"/> を実行します
        /// </summary>
        /// <param name="effect">対象の <see cref="EffectElement"/></param>
        /// <param name="value">セットする値</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.CheckCommand Check(this EffectElement effect, bool value, CommandMode mode = CommandMode.Recode) {
            var command = new EffectElement.CheckCommand(effect, value);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.UpCommand"/> を実行します
        /// </summary>
        /// <param name="effect">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.UpCommand Up(this EffectElement effect, CommandMode mode = CommandMode.Recode) {
            var command = new EffectElement.UpCommand(effect);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.DownCommand"/> を実行します
        /// </summary>
        /// <param name="effect">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.DownCommand Down(this EffectElement effect, CommandMode mode = CommandMode.Recode) {
            var command = new EffectElement.DownCommand(effect);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.RemoveCommand"/> を実行します
        /// </summary>
        /// <param name="effect">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.RemoveCommand Remove(this EffectElement effect, CommandMode mode = CommandMode.Recode) {
            var command = new EffectElement.RemoveCommand(effect);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.AddCommand"/> を実行します
        /// </summary>
        /// <param name="effect">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.AddCommand Add(this EffectElement effect, CommandMode mode = CommandMode.Recode) {
            var command = new EffectElement.AddCommand(effect);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.DownCommand"/> を実行します
        /// </summary>
        /// <param name="effect">対象の <see cref="EffectElement"/></param>
        /// <param name="clip"></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.AddCommand Add(this EffectElement effect, ClipData clip, CommandMode mode = CommandMode.Recode) {
            var command = new EffectElement.AddCommand(effect, clip);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }


        /// <summary>
        /// <see cref="ClipData.AddCommand"/> を実行します
        /// </summary>
        /// <param name="scene">対象の <see cref="Scene"/></param>
        /// <param name="addframe">配置するフレーム</param>
        /// <param name="layer">配置するレイヤー</param>
        /// <param name="type">クリップの種類</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="scene"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="addframe"/> が0以下です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.AddCommand Add(this Scene scene, int addframe, int layer, Type type, CommandMode mode = CommandMode.Recode) {
            var command = new ClipData.AddCommand(scene, addframe, layer, type);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.RemoveCommand"/> を実行します
        /// </summary>
        /// <param name="clip">対象の <see cref="ClipData"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.RemoveCommand Remove(this ClipData clip, CommandMode mode = CommandMode.Recode) {
            var command = new ClipData.RemoveCommand(clip);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.MoveCommand"/> を実行します
        /// </summary>
        /// <param name="clip">対象の <see cref="ClipData"/></param>
        /// <param name="to">新しい開始フレーム</param>
        /// <param name="tolayer">新しい配置レイヤー</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> または <paramref name="tolayer"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.MoveCommand Move(this ClipData clip, int to, int tolayer, CommandMode mode = CommandMode.Recode) {
            var command = new ClipData.MoveCommand(clip, to, tolayer);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.MoveCommand"/> を実行します
        /// </summary>
        /// <param name="clip">対象の <see cref="ClipData"/></param>
        /// <param name="to">新しい開始フレーム</param>
        /// <param name="from">古い開始フレーム</param>
        /// <param name="tolayer">新しい配置レイヤー</param>
        /// <param name="fromlayer">古い配置レイヤー</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.MoveCommand Move(this ClipData clip, int to, int from, int tolayer, int fromlayer, CommandMode mode = CommandMode.Recode) {
            var command = new ClipData.MoveCommand(clip, to, from, tolayer, fromlayer);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.LengthChangeCommand"/> を実行します
        /// </summary>
        /// <param name="clip">対象の <see cref="ClipData"/></param>
        /// <param name="start">開始フレーム</param>
        /// <param name="end">終了フレーム</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="UndoRedoManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> または <paramref name="end"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.LengthChangeCommand LengthChange(this ClipData clip, int start, int end, CommandMode mode = CommandMode.Recode) {
            var command = new ClipData.LengthChangeCommand(clip, start, end);
            if (mode == CommandMode.Recode) {
                UndoRedoManager.Do(command);
            }
            else {
                command.Do();
            }
            return command;
        }
    }

    public enum CommandMode {
        Recode,
        Execute
    }
}
