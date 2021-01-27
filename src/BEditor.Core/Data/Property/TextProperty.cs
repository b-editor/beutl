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

namespace BEditor.Core.Data.Property
{
    [DataContract]
    public class TextProperty : PropertyElement<TextPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _ValueArgs = new(nameof(Value));
        private string _Value;
        private List<IObserver<string>>? _List;
        private IDisposable? _BindDispose;
        private IBindable<string>? _Bindable;
        private string? _BindHint;
        #endregion


        public TextProperty(TextPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Value = metadata.DefaultText;
        }


        private List<IObserver<string>> Collection => _List ??= new();
        /// <inheritdoc/>
        [DataMember]
        public string Value
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
        public void Bind(IBindable<string>? bindable)
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
        [Pure]
        public IRecordCommand ChangeText(string text) => new ChangeTextCommand(this, text);
        #endregion


        private sealed class ChangeTextCommand : IRecordCommand
        {
            private readonly TextProperty _Property;
            private readonly string _New;
            private readonly string _Old;

            public ChangeTextCommand(TextProperty property, string value)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Old = property.Value;
                _New = value;
            }

            public string Name => CommandName.ChangeText;

            public void Do() => _Property.Value = _New;
            public void Redo() => Do();
            public void Undo() => _Property.Value = _Old;
        }
    }

    public record TextPropertyMetadata(string Name, string DefaultText = "") : PropertyElementMetadata(Name);
}
