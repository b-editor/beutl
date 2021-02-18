using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property of a multi-line string.
    /// </summary>
    [DataContract]
    public class DocumentProperty : PropertyElement<DocumentPropertyMetadata>, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _textArgs = new(nameof(Text));
        private string _text = "";
        private List<IObserver<string>>? _list;

        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private string? _bindHint;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Text = metadata.DefaultText;
        }


        private List<IObserver<string>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the string being entered.
        /// </summary>
        [DataMember]
        public string Text
        {
            get => _text;
            set => SetValue(value, ref _text, _textArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._text);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public string Value => Text;
        /// <inheritdoc/>
        [DataMember]
        public string? BindHint
        {
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }


        #region Methods

        #region IBindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(string value)
        {
            Text = value;
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
        public void Bind(IBindable<string>? bindable)
        {
            _bindDispose?.Dispose();
            _bindable = bindable;

            if (bindable is not null)
            {
                Text = bindable.Value;

                // bindableが変更時にthisが変更
                _bindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_bindHint is not null && this.GetBindable(_bindHint, out var b))
            {
                Bind(b);
            }
            _bindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// Create a command to change the string.
        /// </summary>
        /// <param name="newtext">New value for <see cref="Text"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeText(string newtext) => new TextChangeCommand(this, newtext);

        #endregion


        #region Commands

        private sealed class TextChangeCommand : IRecordCommand
        {
            private readonly DocumentProperty _Property;
            private readonly string _New;
            private readonly string _Old;

            public TextChangeCommand(DocumentProperty property, string text)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = text;
                _Old = property.Text;
            }

            public string Name => CommandName.ChangeText;

            public void Do() => _Property.Text = _New;
            public void Redo() => Do();
            public void Undo() => _Property.Text = _Old;
        }

        #endregion
    }

    /// <summary>
    /// Represents the metadata of a <see cref="DocumentProperty"/>.
    /// </summary>
    public record DocumentPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentPropertyMetadata"/> class.
        /// </summary>
        /// <param name="DefaultText">Default value for <see cref="DocumentProperty.Text"/>.</param>
        public DocumentPropertyMetadata(string DefaultText) : base(string.Empty)
        {
            this.DefaultText = DefaultText;
        }

        /// <summary>
        /// Get the default value of <see cref="DocumentProperty.Text"/>.
        /// </summary>
        public string DefaultText { get; init; }
    }
}
