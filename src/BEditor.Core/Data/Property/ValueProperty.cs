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

    public record ValuePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN)
        : PropertyElementMetadata(Name);
}
