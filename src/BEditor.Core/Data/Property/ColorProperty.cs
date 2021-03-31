using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Drawing;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property to pick a color.
    /// </summary>
    [DebuggerDisplay("Color = {_color:#argb}")]
    public class ColorProperty : PropertyElement<ColorPropertyMetadata>, IEasingProperty, IBindable<Color>
    {
        #region Fields
        private Color _value;
        private List<IObserver<Color>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<Color>? _bindable;
        private string? _targetHint;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ColorProperty(ColorPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Value = metadata.DefaultColor;
        }

        /// <summary>
        /// Gets or sets the selected color.
        /// </summary>
        public Color Value
        {
            get => _value;
            set => SetValue(value, ref _value, DocumentProperty._valueArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._value);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }

        /// <inheritdoc/>
        public string? TargetHint
        {
            get => _bindable?.ToString("#");
            private set => _targetHint = value;
        }

        private List<IObserver<Color>> Collection => _list ??= new();

        #region Methods

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Value), Value.ToString("#argb"));
            writer.WriteString(nameof(TargetHint), TargetHint);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? Color.FromHTML(value.GetString()) : Color.Light;
            TargetHint = element.TryGetProperty(nameof(TargetHint), out var bind) ? bind.GetString() : null;
        }

        /// <summary>
        /// Create a command to change the color of this <see cref="Color"/>.
        /// </summary>
        /// <param name="color">New Color.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeColor(Color color) => new ChangeColorCommand(this, color);

        /// <inheritdoc/>
        public void Bind(IBindable<Color>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<Color> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public void OnCompleted()
        {
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
        }

        /// <inheritdoc/>
        public void OnNext(Color value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetHint);
        }

        #endregion

        /// <summary>
        /// 色を変更するコマンド.
        /// </summary>
        private sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly WeakReference<ColorProperty> _property;
            private readonly Color _new;
            private readonly Color _old;

            /// <summary>
            /// <see cref="ChangeColorCommand"/> クラスの新しいインスタンスを初期化します.
            /// </summary>
            /// <param name="property">対象の <see cref="ColorProperty"/>.</param>
            /// <param name="color"><see cref="ColorProperty.Value"/> の新しい値.</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です.</exception>
            public ChangeColorCommand(ColorProperty property, Color color)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = color;
                _old = property.Value;
            }

            /// <inheritdoc/>
            public string Name => Strings.ChangeColor;

            /// <inheritdoc/>
            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _new;
                }
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _old;
                }
            }
        }
    }
}
