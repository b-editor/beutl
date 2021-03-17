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

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property of a string.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Value = {Value}")]
    public class TextProperty : PropertyElement<TextPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _valueArgs = new(nameof(Value));
        private string _value;
        private List<IObserver<string>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private string? _bindHint;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="TextProperty"/> class.
        /// </summary>
        /// <param name="metadata">Matadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public TextProperty(TextPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _value = metadata.DefaultText;
        }


        private List<IObserver<string>> Collection => _list ??= new();
        /// <inheritdoc/>
        [DataMember]
        public string Value
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
        public void Bind(IBindable<string>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
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
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _bindHint);
        }

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
            private readonly WeakReference<TextProperty> _property;
            private readonly string _new;
            private readonly string _old;

            public ChangeTextCommand(TextProperty property, string value)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _old = property.Value;
                _new = value;
            }

            public string Name => CommandName.ChangeText;

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

    /// <summary>
    /// Represents the metadata of a <see cref="TextProperty"/>.
    /// </summary>
    public record TextPropertyMetadata : PropertyElementMetadata, IPropertyBuilder<TextProperty>
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

        /// <inheritdoc/>
        public TextProperty Build()
        {
            return new(this);
        }
    }
}
