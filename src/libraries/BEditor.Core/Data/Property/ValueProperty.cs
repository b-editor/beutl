// ValueProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
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
        private static readonly PropertyChangedEventArgs _valueArgs = new(nameof(Value));
        private float _value;
        private List<IObserver<float>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<float>? _bindable;
        private Guid? _targetID;

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
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        private List<IObserver<float>> Collection => _list ??= new();

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

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? value.GetSingle() : 0;
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
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
            this.AutoLoad(ref _targetID);
        }

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