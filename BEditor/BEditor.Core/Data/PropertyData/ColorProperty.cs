using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;

namespace BEditor.Core.Data.PropertyData
{
    //Todo : ColorPropertyにIObserver, IObserbleを実装
    /// <summary>
    /// 色を選択するプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class ColorProperty : PropertyElement
    {
        private static readonly PropertyChangedEventArgs rArgs = new(nameof(Red));
        private static readonly PropertyChangedEventArgs gArgs = new(nameof(Green));
        private static readonly PropertyChangedEventArgs bArgs = new(nameof(Blue));
        private static readonly PropertyChangedEventArgs aArgs = new(nameof(Alpha));
        private byte r;
        private byte g;
        private byte b;
        private byte a;

        /// <summary>
        /// <see cref="ColorProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="ColorPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public ColorProperty(ColorPropertyMetadata metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            Red = metadata.Red;
            Green = metadata.Green;
            Blue = metadata.Blue;
            Alpha = metadata.Alpha;
            PropertyMetadata = metadata;
        }

        /// <summary>
        /// Red
        /// </summary>
        [DataMember]
        public byte Red
        {
            get => r;
            set => SetValue(value, ref r, rArgs);
        }
        /// <summary>
        /// Green
        /// </summary>
        [DataMember]
        public byte Green
        {
            get => g;
            set => SetValue(value, ref g, gArgs);
        }
        /// <summary>
        /// Blue
        /// </summary>
        [DataMember]
        public byte Blue
        {
            get => b;
            set => SetValue(value, ref b, bArgs);
        }
        /// <summary>
        /// Alpha
        /// </summary>
        [DataMember]
        public byte Alpha
        {
            get => a;
            set => SetValue(value, ref a, aArgs);
        }

        public static implicit operator Media.Color(ColorProperty val) => new(val.Red, val.Green, val.Blue, val.Alpha);
        /// <inheritdoc/>
        public override string ToString() => $"(R:{Red} G:{Green} B:{Blue} A:{Alpha} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// 色を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeColorCommand : IUndoRedoCommand
        {
            private readonly ColorProperty Color;
            private readonly byte r, g, b, a;
            private readonly byte or, og, ob, oa;

            /// <summary>
            /// <see cref="ChangeColorCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="ColorProperty"/></param>
            /// <param name="r">新しい <see cref="Red"/> の値</param>
            /// <param name="g">新しい <see cref="Green"/> の値</param>
            /// <param name="b">新しい <see cref="Blue"/> の値</param>
            /// <param name="a">新しい <see cref="Alpha"/> の値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeColorCommand(ColorProperty property, byte r, byte g, byte b, byte a)
            {
                Color = property ?? throw new ArgumentNullException(nameof(property));
                (this.r, this.g, this.b, this.a) = (r, g, b, a);
                (or, og, ob, oa) = (property.Red, property.Green, property.Blue, property.Alpha);
            }


            /// <inheritdoc/>
            public void Do()
            {
                Color.Red = r;
                Color.Green = g;
                Color.Blue = b;
                Color.Alpha = a;
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                Color.Red = or;
                Color.Green = og;
                Color.Blue = ob;
                Color.Alpha = oa;
            }
        }
    }

    /// <summary>
    /// <see cref="ColorProperty"/> のメタデータを表します
    /// </summary>
    public record ColorPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// <see cref="ColorPropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public ColorPropertyMetadata(string name, byte r = 255, byte g = 255, byte b = 255, byte a = 255, bool usealpha = false) : base(name)
        {
            Red = r;
            Green = g;
            Blue = b;
            Alpha = a;
            UseAlpha = usealpha;
        }

        /// <summary>
        /// <see cref="ColorProperty.Red"/> のデフォルト値を取得します
        /// </summary>
        public byte Red { get; init; }
        /// <summary>
        /// <see cref="ColorProperty.Green"/> のデフォルト値を取得します
        /// </summary>
        public byte Green { get; init; }
        /// <summary>
        /// <see cref="ColorProperty.Blue"/> のデフォルト値を取得します
        /// </summary>
        public byte Blue { get; init; }
        /// <summary>
        /// <see cref="ColorProperty.Alpha"/> のデフォルト値を取得します
        /// </summary>
        public byte Alpha { get; init; }
        /// <summary>
        /// Alphaチャンネルを表示する場合 <see langword="true"/>、そうでない場合は <see langword="false"/> となります
        /// </summary>
        public bool UseAlpha { get; init; }
    }
}
