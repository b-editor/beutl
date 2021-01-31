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
    /// <summary>
    /// Represents a property of a string.
    /// </summary>
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


        /// <summary>
        /// Initializes a new instance of the <see cref="TextProperty"/> class.
        /// </summary>
        /// <param name="metadata">Matadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
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
        /// <summary>
        /// Create a command to change the <see cref="Value"/>.
        /// </summary>
        /// <param name="text">New value for <see cref="Value"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
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

    /// <summary>
    /// Represents the metadata of a <see cref="TextProperty"/>.
    /// </summary>
    public record TextPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TextPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultText">Default value for <see cref="TextProperty.Value"/>.</param>
        public TextPropertyMetadata(string Name, string DefaultText = "") : base(Name)
        {
            this.DefaultText = DefaultText;
        }

        /// <summary>
        /// Get the default value of <see cref="TextProperty.Value"/>.
        /// </summary>
        public string DefaultText { get; init; }
    }
}
