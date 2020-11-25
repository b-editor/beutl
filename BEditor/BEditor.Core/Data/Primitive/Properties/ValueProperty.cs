using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Primitive.Properties
{
    public class ValueProperty : PropertyElement<ValuePropertyMetadata>, IBindable<float>, IEasingProperty
    {
        #region Fields
        private static readonly PropertyChangedEventArgs valueArgs = new(nameof(Value));
        private float value;
        private List<IObserver<float>> list;
        private IDisposable BindDispose;
        #endregion

        public ValueProperty(ValuePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            value = metadata.DefaultValue;
        }

        private List<IObserver<float>> Collection => list ??= new();
        /// <inheritdoc/>
        [DataMember]
        public float Value
        {
            get => value;
            set => SetValue(value, ref this.value, valueArgs, () =>
            {
                foreach (var observer in Collection)
                {
                    try
                    {
                        observer.OnNext(this.value);
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
        [DataMember]
        public string BindHint { get; private set; }

        #region Methods

        /// <inheritdoc/>
        public void Bind(IBindable<float> bindable)
        {
            BindDispose?.Dispose();
            BindHint = null;

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                Value = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }
        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(float value)
        {
            this.Value = value;
        }
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<float> observer)
        {
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }
        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();

            if (BindHint is not null)
            {
                if (this.GetBindable(BindHint, out var b))
                {
                    Bind(b);
                }
            }
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Value:{Value} Name:{PropertyMetadata?.Name})";
        public float InRange(float value)
        {
            var constant = PropertyMetadata;
            var max = constant.Max;
            var min = constant.Min;

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

        #endregion

        public sealed class ChangeValueCommand : IRecordCommand
        {
            private readonly ValueProperty property;
            private readonly float value;
            private readonly float old;

            public ChangeValueCommand(ValueProperty property, float value)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.old = property.Value;
                this.value = property.InRange(value);
            }

            public void Do() => property.Value = value;
            public void Redo() => Do();
            public void Undo() => property.Value = old;
        }
    }

    public record ValuePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN)
        : PropertyElementMetadata(Name);
}
