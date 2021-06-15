// DocumentProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property of a multi-line string.
    /// </summary>
    [DebuggerDisplay("Text = {Value}")]
    public class DocumentProperty : PropertyElement<DocumentPropertyMetadata>, IBindable<string>
    {
        /// <summary>
        /// <see cref="IBindable{T}.Value"/> のプロパティの変更を通知するイベントの引数.
        /// </summary>
        internal static readonly PropertyChangedEventArgs _valueArgs = new(nameof(Value));
        private string _value = string.Empty;
        private List<IObserver<string>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private Guid? _targetID;

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Value = metadata.DefaultText;
        }

        /// <summary>
        /// Gets or sets the string being entered.
        /// </summary>
        public string Value
        {
            get => _value;
            set
            {
                if (SetAndRaise(value, ref _value, _valueArgs))
                {
                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(_value);
                        }
                        catch (Exception ex)
                        {
                            observer.OnError(ex);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        private List<IObserver<string>> Collection => _list ??= new();

        /// <inheritdoc/>
        public void OnCompleted()
        {
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
        }

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
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Value), Value);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? value.GetString() ?? string.Empty : string.Empty;
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
        }

        /// <summary>
        /// Create a command to change the string.
        /// </summary>
        /// <param name="newtext">New value for <see cref="Value"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeText(string newtext)
        {
            return new TextChangeCommand(this, newtext);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetID);
        }

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

            public string Name => Strings.ChangeText;

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
}