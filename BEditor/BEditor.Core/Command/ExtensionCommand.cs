using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Command
{
    public enum CommandMode
    {
        Recode,
        Execute
    }

    public static class ExtensionCommand
    {
        /// <summary>
        /// <see cref="EffectElement.CheckCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="EffectElement"/></param>
        /// <param name="value">セットする値</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.CheckCommand Check(this EffectElement self, bool value, CommandMode mode = CommandMode.Recode)
        {
            var command = new EffectElement.CheckCommand(self, value);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.UpCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.UpCommand Up(this EffectElement self, CommandMode mode = CommandMode.Recode)
        {
            var command = new EffectElement.UpCommand(self);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.DownCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.DownCommand Down(this EffectElement self, CommandMode mode = CommandMode.Recode)
        {
            var command = new EffectElement.DownCommand(self);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.RemoveCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.RemoveCommand Remove(this EffectElement self, CommandMode mode = CommandMode.Recode)
        {
            var command = new EffectElement.RemoveCommand(self);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.AddCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="EffectElement"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.AddCommand Add(this EffectElement self, CommandMode mode = CommandMode.Recode)
        {
            var command = new EffectElement.AddCommand(self);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="EffectElement.DownCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="EffectElement"/></param>
        /// <param name="clip"></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static EffectElement.AddCommand Add(this EffectElement self, ClipData clip, CommandMode mode = CommandMode.Recode)
        {
            var command = new EffectElement.AddCommand(self, clip);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }


        /// <summary>
        /// <see cref="ClipData.AddCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="Scene"/></param>
        /// <param name="addframe">配置するフレーム</param>
        /// <param name="layer">配置するレイヤー</param>
        /// <param name="metadata">クリップの種類</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="addframe"/> が0以下です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.AddCommand Add(this Scene self, int addframe, int layer, ObjectMetadata metadata, CommandMode mode = CommandMode.Recode)
        {
            var command = new ClipData.AddCommand(self, addframe, layer, metadata);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.RemoveCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="ClipData"/></param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.RemoveCommand Remove(this ClipData self, CommandMode mode = CommandMode.Recode)
        {
            var command = new ClipData.RemoveCommand(self);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.MoveCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="ClipData"/></param>
        /// <param name="to">新しい開始フレーム</param>
        /// <param name="tolayer">新しい配置レイヤー</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> または <paramref name="tolayer"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.MoveCommand Move(this ClipData self, int to, int tolayer, CommandMode mode = CommandMode.Recode)
        {
            var command = new ClipData.MoveCommand(self, to, tolayer);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.MoveCommand"/> を実行します
        /// </summary>
        /// <param name="self">対象の <see cref="ClipData"/></param>
        /// <param name="to">新しい開始フレーム</param>
        /// <param name="from">古い開始フレーム</param>
        /// <param name="tolayer">新しい配置レイヤー</param>
        /// <param name="fromlayer">古い配置レイヤー</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.MoveCommand Move(this ClipData self, int to, int from, int tolayer, int fromlayer, CommandMode mode = CommandMode.Recode)
        {
            var command = new ClipData.MoveCommand(self, to, from, tolayer, fromlayer);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }
        /// <summary>
        /// <see cref="ClipData.LengthChangeCommand"/> を実行します
        /// </summary>
        /// <param name="elf">対象の <see cref="ClipData"/></param>
        /// <param name="start">開始フレーム</param>
        /// <param name="end">終了フレーム</param>
        /// <param name="mode"><see cref="CommandMode.Execute"/> を指定するとコマンドが <see cref="CommandManager"/> に記録されません</param>
        /// <exception cref="ArgumentNullException"><paramref name="elf"/> が <see langword="null"/> です</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> または <paramref name="end"/> が0以下です</exception>
        /// <returns>作成されたコマンド</returns>
        public static ClipData.LengthChangeCommand LengthChange(this ClipData elf, int start, int end, CommandMode mode = CommandMode.Recode)
        {
            var command = new ClipData.LengthChangeCommand(elf, start, end);
            if (mode == CommandMode.Recode)
            {
                CommandManager.Do(command);
            }
            else
            {
                command.Do();
            }
            return command;
        }

        internal static void ExecuteLoaded(this PropertyElement property, PropertyElementMetadata metadata)
        {
            property.PropertyLoaded();
            property.PropertyMetadata = metadata;
        }
        internal static void ExecuteLoaded<T>(this PropertyElement<T> property, T metadata) where T : PropertyElementMetadata
        {
            property.PropertyLoaded();
            property.PropertyMetadata = metadata;
        }
    }
}
