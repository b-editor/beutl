using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Drawing;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// 色を選択するプロパティを表します
    /// </summary>
    [DataContract]
    public class ColorProperty : PropertyElement<ColorPropertyMetadata>, IEasingProperty, IBindable<Color>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs colorArgs = new(nameof(Color));
        private Color color;
        private List<IObserver<Color>> list;

        private IDisposable BindDispose;
        private IBindable<Color> Bindable;
        private string bindHint;

        #endregion


        /// <summary>
        /// <see cref="ColorProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="ColorPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public ColorProperty(ColorPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Color = metadata.DefaultColor;
        }


        private List<IObserver<Color>> Collection => list ??= new();
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public Color Color
        {
            get => color;
            set => SetValue(value, ref color, colorArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.color);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public Color Value => color;
        /// <inheritdoc/>
        [DataMember]
        public string BindHint
        {
            get => Bindable?.GetString();
            private set => bindHint = value;
        }


        #region Methods

        /// <inheritdoc/>
        public override string ToString() => $"(R:{color.R} G:{color.G} B:{color.B} A:{color.A} Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (bindHint is not null && this.GetBindable(bindHint, out var b))
            {
                Bind(b);
            }
            bindHint = null;
        }

        #region IBindable

        public void Bind(IBindable<Color>? bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
                Color = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        public IDisposable Subscribe(IObserver<Color> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
             {
                 state.observer.OnCompleted();
                 state.Item2.Collection.Remove(state.observer);
             });
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(Color value)
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
            private readonly ColorProperty property;
            private readonly Color @new;
            private readonly Color old;

            /// <summary>
            /// <see cref="ChangeColorCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="ColorProperty"/></param>
            /// <param name="color"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeColorCommand(ColorProperty property, in Color color)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                @new = color;
                old = property.Value;
            }

            public string Name => CommandName.ChangeColor;

            /// <inheritdoc/>
            public void Do()
            {
                property.Color = @new;
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                property.Color = old;
            }
        }
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Property.ColorProperty"/> のメタデータを表します
    /// </summary>
    public record ColorPropertyMetadata(string Name, in Color DefaultColor = default, bool UseAlpha = false) : PropertyElementMetadata(Name);
}
