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

namespace BEditor.Core.Data.Property
{
    [DataContract]
    public class TextProperty : PropertyElement<TextPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs valueArgs = new(nameof(Value));
        private string value;
        private List<IObserver<string>> list;
        private IDisposable BindDispose;
        private IBindable<string> Bindable;
        private string bindHint;
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
            set => SetValue(value, ref this.value, valueArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.value);
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
        public string BindHint 
        {
            get => Bindable?.GetString();
            private set => bindHint = value;
        }

        #region Methods
        /// <inheritdoc/>
        public void Bind(IBindable<string>? bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
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
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }
        /// <inheritdoc/>
        public override void Loaded()
        {
            if (IsLoaded) return;
            if (bindHint is not null)
            {
                if (this.GetBindable(bindHint, out var b))
                {
                    Bind(b);
                }
            }
            bindHint = null;

            base.Loaded();
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Value:{Value} Name:{PropertyMetadata?.Name})";
        #endregion

        public sealed class ChangeTextCommand : IRecordCommand
        {
            private readonly TextProperty property;
            private readonly string @new;
            private readonly string old;

            public ChangeTextCommand(TextProperty property, string value)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.old = property.Value;
                this.@new = value;
            }

            public void Do() => property.Value = @new;
            public void Redo() => Do();
            public void Undo() => property.Value = old;
        }
    }

    public record TextPropertyMetadata(string Name, string DefaultText = "") : PropertyElementMetadata(Name);
}
