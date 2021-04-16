using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property of <see cref="float"/> type.
    /// </summary>
    [DebuggerDisplay("Value = {Value}")]
    public class ValueProperty : PropertyElement<ValuePropertyMetadata>, IBindable<float>, IEasingProperty
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _valueArgs = new(nameof(Value));
        private float _value;
        private List<IObserver<float>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<float>? _bindable;
        private string? _bindHint;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ValueProperty"/> class.
        /// </summary>
        /// <param name="metadata">Matadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ValueProperty(ValuePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _value = metadata.DefaultValue;
        }

        /// <inheritdoc/>
        public float Value
        {
            get => _value;
            set => SetValue(value, ref _value, _valueArgs, this, state =>
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
            private set => _bindHint = value;
        }

        private List<IObserver<float>> Collection => _list ??= new();

        #region Methods

        /// <inheritdoc/>
        public void Bind(IBindable<float>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
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
        public void OnNext(float value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<float> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Value), Value);
            writer.WriteString(nameof(TargetHint), TargetHint);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? value.GetSingle() : 0;
            TargetHint = element.TryGetProperty(nameof(TargetHint), out var bind) ? bind.GetString() : null;
        }

        /// <summary>
        /// Returns <paramref name="value"/> clamped to the inclusive range of <see cref="ValuePropertyMetadata.Min"/> and <see cref="ValuePropertyMetadata.Max"/>.
        /// </summary>
        /// <param name="value">The value to be clamped.</param>
        /// <returns>value if min ≤ value ≤ max. -or- min if value &lt; min. -or- max if max &lt; value.</returns>
        public float Clamp(float value)
        {
            var meta = PropertyMetadata;
            var max = meta?.Max ?? float.NaN;
            var min = meta?.Min ?? float.NaN;

            if (!float.IsNaN(min) && value <= min)
            {
                return min;
            }
            else if (!float.IsNaN(max) && max <= value)
            {
                return max;
            }

            return value;
        }

        /// <summary>
        /// Create a command to change the <see cref="Value"/>.
        /// </summary>
        /// <param name="value">New value for <see cref="Value"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeValue(float value) => new ChangeValueCommand(this, value);

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _bindHint);
        }

        #endregion

        private sealed class ChangeValueCommand : IRecordCommand
        {
            private readonly WeakReference<ValueProperty> _property;
            private readonly float _new;
            private readonly float _old;

            public ChangeValueCommand(ValueProperty property, float value)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _old = property.Value;
                _new = property.Clamp(value);
            }

            public string Name => Strings.ChangeValue;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _new;
                }
            }

            public void Redo()
            {
                Do();
            }

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