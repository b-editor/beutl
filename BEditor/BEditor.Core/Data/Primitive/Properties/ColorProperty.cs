using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Media;

namespace BEditor.Core.Data.Primitive.Properties
{
    //Todo : ColorPropertyにIObserver, IObserbleを実装
    /// <summary>
    /// 色を選択するプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class ColorProperty : PropertyElement, IEasingProperty, IBindable<ReadOnlyColor>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs colorArgs = new(nameof(Color));
        private Color color;
        private List<IObserver<ReadOnlyColor>> list;

        private IDisposable BindDispose;

        #endregion


        /// <summary>
        /// <see cref="ColorProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="ColorPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public ColorProperty(ColorPropertyMetadata metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            Color = new(metadata.Red, metadata.Green, metadata.Blue, metadata.Alpha);
            PropertyMetadata = metadata;
        }


        private List<IObserver<ReadOnlyColor>> collection => list ??= new();
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public Color Color
        {
            get => color;
            set => SetValue(value, ref color, colorArgs, () =>
            {
                foreach (var observer in collection)
                {
                    try
                    {
                        observer.OnNext(color);
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public ReadOnlyColor Value => color;
        /// <inheritdoc/>
        [DataMember]
        public string BindHint { get; private set; }


        #region Methods

        /// <inheritdoc/>
        public override string ToString() => $"(R:{color.R} G:{color.G} B:{color.B} A:{color.A} Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();

            if (BindHint is not null && this.GetBindable(BindHint, out var b))
            {
                Bind(b);
            }
        }

        #region IBindable

        public void Bind(IBindable<ReadOnlyColor> bindable)
        {
            BindDispose?.Dispose();

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                Color = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        public IDisposable Subscribe(IObserver<ReadOnlyColor> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(ReadOnlyColor value)
        {
            Color = value;
        }

        #endregion

        #endregion


        /// <summary>
        /// 色を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly ColorProperty Color;
            private readonly ReadOnlyColor newest;
            private readonly ReadOnlyColor old;

            /// <summary>
            /// <see cref="ChangeColorCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="ColorProperty"/></param>
            /// <param name="color"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeColorCommand(ColorProperty property, in ReadOnlyColor color)
            {
                Color = property ?? throw new ArgumentNullException(nameof(property));
                newest = color;
                old = property.Value;
            }


            /// <inheritdoc/>
            public void Do()
            {
                Color.Color = newest;
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                Color.Color = old;
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


        public byte Red { get; init; }

        public byte Green { get; init; }

        public byte Blue { get; init; }

        public byte Alpha { get; init; }

        public bool UseAlpha { get; init; }
    }
}
