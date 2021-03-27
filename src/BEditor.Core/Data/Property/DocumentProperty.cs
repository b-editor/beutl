using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property of a multi-line string.
    /// </summary>
    [DebuggerDisplay("Text = {Value}")]
    public class DocumentProperty : PropertyElement<DocumentPropertyMetadata>, IBindable<string>
    {
        #region Fields
        internal static readonly PropertyChangedEventArgs _valueArgs = new(nameof(Value));
        private string _value = "";
        private List<IObserver<string>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private string? _targetHint;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Value = metadata.DefaultText;
        }

        private List<IObserver<string>> Collection => _list ??= new();

        /// <summary>
        /// Gets or sets the string being entered.
        /// </summary>
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
        public string? TargetHint
        {
            get => _bindable?.GetString();
            private set => _targetHint = value;
        }

        #region Methods

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
        public void Bind(IBindable<string>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetHint);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Value), Value);
            writer.WriteString(nameof(TargetHint), TargetHint);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? value.GetString() ?? "" : "";
            TargetHint = element.TryGetProperty(nameof(TargetHint), out var bind) ? bind.GetString() : null;
        }

        /// <summary>
        /// Create a command to change the string.
        /// </summary>
        /// <param name="newtext">New value for <see cref="Value"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeText(string newtext) => new TextChangeCommand(this, newtext);

        #endregion


        #region Commands

        private sealed class TextChangeCommand : IRecordCommand
        {
            private readonly WeakReference<DocumentProperty> _property;
            private readonly string _new;
            private readonly string _old;

            public TextChangeCommand(DocumentProperty property, string value)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = value;
                _old = property.Value;
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

        #endregion
    }
}
