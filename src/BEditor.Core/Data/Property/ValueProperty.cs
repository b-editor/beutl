using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property of <see cref="float"/> type.
    /// </summary>
    [DataContract]
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
        /// <param name="metadata">Matadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ValueProperty(ValuePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _value = metadata.DefaultValue;
        }


        private List<IObserver<float>> Collection => _list ??= new();
        /// <inheritdoc/>
        [DataMember]
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
        [DataMember]
        public string? BindHint
        {
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }


        #region Methods

        /// <inheritdoc/>
        public void Bind(IBindable<float>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        /// <inheritdoc/>
        public void OnCompleted() { }

        /// <inheritdoc/>
        public void OnError(Exception error) { }

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
        protected override void OnLoad()
        {
            this.AutoLoad(ref _bindHint);
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
        /// <param name="value">New value for <see cref="Value"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeValue(float value) => new ChangeValueCommand(this, value);

        #endregion


        private sealed class ChangeValueCommand : IRecordCommand
        {
            private readonly WeakReference<ValueProperty> _Property;
            private readonly float _New;
            private readonly float _Old;

            public ChangeValueCommand(ValueProperty property, float value)
            {
                _Property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _Old = property.Value;
                _New = property.Clamp(value);
            }

            public string Name => CommandName.ChangeValue;

            public void Do()
            {
                if (_Property.TryGetTarget(out var target))
                {
                    target.Value = _New;
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                if (_Property.TryGetTarget(out var target))
                {
                    target.Value = _Old;
                }
            }
        }
    }

    /// <summary>
    /// Represents the metadata of a <see cref="ValueProperty"/>.
    /// </summary>
    public record ValuePropertyMetadata : PropertyElementMetadata, IPropertyBuilder<ValueProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ValuePropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultValue">Default value</param>
        /// <param name="Max">Maximum value.</param>
        /// <param name="Min">Minimum value</param>
        public ValuePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN) : base(Name)
        {
            this.DefaultValue = DefaultValue;
            this.Max = Max;
            this.Min = Min;
        }

        /// <summary>
        /// Gets the default value.
        /// </summary>
        public float DefaultValue { get; init; }
        /// <summary>
        /// Gets the maximum value.
        /// </summary>
        public float Max { get; init; }
        /// <summary>
        /// Get the minimum value.
        /// </summary>
        public float Min { get; init; }

        /// <inheritdoc/>
        public ValueProperty Build()
        {
            return new(this);
        }
    }
}
