using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a property of <see cref="float"/> type.
    /// </summary>
    [DataContract]
    public class ValueProperty : PropertyElement<ValuePropertyMetadata>, IBindable<float>, IEasingProperty
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _ValueArgs = new(nameof(Value));
        private float _Value;
        private List<IObserver<float>>? _List;
        private IDisposable? _BindDispose;
        private IBindable<float>? _Bindable;
        private string? _BindHint;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="ValueProperty"/> class.
        /// </summary>
        /// <param name="metadata">Matadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ValueProperty(ValuePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Value = metadata.DefaultValue;
        }


        private List<IObserver<float>> Collection => _List ??= new();
        /// <inheritdoc/>
        [DataMember]
        public float Value
        {
            get => _Value;
            set => SetValue(value, ref _Value, _ValueArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._Value);
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
            get => _Bindable?.GetString();
            private set => _BindHint = value;
        }


        #region Methods

        /// <inheritdoc/>
        public void Bind(IBindable<float>? bindable)
        {
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                Value = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
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
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_BindHint is not null)
            {
                if (this.GetBindable(_BindHint, out var b))
                {
                    Bind(b);
                }
            }
            _BindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Value:{Value} Name:{PropertyMetadata?.Name})";
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
            private readonly ValueProperty _Property;
            private readonly float _New;
            private readonly float _Old;

            public ChangeValueCommand(ValueProperty property, float value)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Old = property.Value;
                _New = property.Clamp(value);
            }

            public string Name => CommandName.ChangeValue;

            public void Do() => _Property.Value = _New;
            public void Redo() => Do();
            public void Undo() => _Property.Value = _Old;
        }
    }

    /// <summary>
    /// Represents the metadata of a <see cref="ValueProperty"/>.
    /// </summary>
    public record ValuePropertyMetadata : PropertyElementMetadata
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
    }
}
