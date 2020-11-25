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
    [DataContract(Namespace = "")]
    public class TextProperty : PropertyElement<TextPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs valueArgs = new(nameof(Value));
        private string value;
        private List<IObserver<string>> list;
        private IDisposable BindDispose;
        #endregion

        public TextProperty(TextPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            value = metadata.DefaultText;
        }

        private List<IObserver<string>> Collection => list ??= new();
        /// <inheritdoc/>
        [DataMember]
        public string Value
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
        public void Bind(IBindable<string> bindable)
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
        public void OnNext(string value)
        {
            Value = value;
        }
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer)
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
        #endregion

        public sealed class ChangeTextCommand : IRecordCommand
        {
            private readonly TextProperty property;
            private readonly string value;
            private readonly string old;

            public ChangeTextCommand(TextProperty property, string value)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.old = property.Value;
                this.value = value;
            }

            public void Do() => property.Value = value;
            public void Redo() => Do();
            public void Undo() => property.Value = old;
        }
    }

    public record TextPropertyMetadata(string Name, string DefaultText = "") : PropertyElementMetadata(Name);
}
