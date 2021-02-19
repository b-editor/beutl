using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Reactive.Disposables;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;
using BEditor.Drawing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property to pick a color.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Color = {_color:#argb}")]
    public class ColorProperty : PropertyElement<ColorPropertyMetadata>, IEasingProperty, IBindable<Color>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _colorArgs = new(nameof(Color));
        private Color _color;
        private List<IObserver<Color>>? _list;

        private IDisposable? _bindDispose;
        private IBindable<Color>? _bindable;
        private string? _bindHint;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="ColorProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ColorProperty(ColorPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Color = metadata.DefaultColor;
        }


        private List<IObserver<Color>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the selected color.
        /// </summary>
        [DataMember]
        public Color Color
        {
            get => _color;
            set => SetValue(value, ref _color, _colorArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._color);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public Color Value => _color;
        /// <inheritdoc/>
        [DataMember]
        public string? BindHint
        {
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }


        #region Methods

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_bindHint is not null && this.GetBindable(_bindHint, out var b))
            {
                Bind(b);
            }
            _bindHint = null;
        }

        /// <summary>
        /// Create a command to change the color of this <see cref="Color"/>.
        /// </summary>
        /// <param name="color">New Color.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeColor(Color color) => new ChangeColorCommand(this, color);

        #region IBindable

        /// <inheritdoc/>
        public void Bind(IBindable<Color>? bindable)
        {
            _bindDispose?.Dispose();
            _bindable = bindable;

            if (bindable is not null)
            {
                Color = bindable.Value;

                // bindableが変更時にthisが変更
                _bindDispose = bindable.Subscribe(this);
            }
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(Color value)
        {
            Color = value;
        }

        #endregion

        #endregion


        /// <summary>
        /// 色を変更するコマンド
        /// </summary>
        private sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly ColorProperty _Property;
            private readonly Color _New;
            private readonly Color _Old;

            /// <summary>
            /// <see cref="ChangeColorCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="ColorProperty"/></param>
            /// <param name="color"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeColorCommand(ColorProperty property, Color color)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = color;
                _Old = property.Value;
            }

            /// <inheritdoc/>
            public string Name => CommandName.ChangeColor;

            /// <inheritdoc/>
            public void Do()
            {
                _Property.Color = _New;
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                _Property.Color = _Old;
            }
        }
    }

    /// <summary>
    /// Represents the metadata of a <see cref="ColorProperty"/>.
    /// </summary>
    public record ColorPropertyMetadata : PropertyElementMetadata, IPropertyBuilder<ColorProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultColor">Default color</param>
        /// <param name="UseAlpha">Value if the alpha component should be used or not</param>
        public ColorPropertyMetadata(string Name, Color DefaultColor, bool UseAlpha = false) : base(Name)
        {
            this.DefaultColor = DefaultColor;
            this.UseAlpha = UseAlpha;
        }

        /// <summary>
        /// Gets the default color.
        /// </summary>
        public Color DefaultColor { get; init; }
        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether or not to use the alpha component.
        /// </summary>
        public bool UseAlpha { get; init; }

        /// <inheritdoc/>
        public ColorProperty Build()
        {
            return new(this);
        }
    }
}
