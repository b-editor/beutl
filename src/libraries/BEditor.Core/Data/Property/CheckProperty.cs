// CheckProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a checkbox property.
    /// </summary>
    [DebuggerDisplay("IsChecked = {Value}")]
    public class CheckProperty : PropertyElement<CheckPropertyMetadata>, IEasingProperty, IBindable<bool>
    {
        private bool _value;
        private List<IObserver<bool>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<bool>? _bindable;
        private Guid? _targetID;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public CheckProperty(CheckPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _value = metadata.DefaultIsChecked;
        }

        /// <inheritdoc/>
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        /// <summary>
        /// Gets or sets the value of whether the item is checked or not.
        /// </summary>
        public bool Value
        {
            get => _value;
            set
            {
                if (SetAndRaise(value, ref _value, DocumentProperty._valueArgs))
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

        private List<IObserver<bool>> Collection => _list ??= new();

        /// <inheritdoc/>
        public void OnCompleted()
        {
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
        }

        /// <inheritdoc/>
        public void OnNext(bool value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
        public IDisposable Subscribe(IObserver<bool> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteBoolean(nameof(Value), Value);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) && value.GetBoolean();
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
        }

        /// <summary>
        /// Create a command to change whether it is checked or not.
        /// </summary>
        /// <param name="value">New value for IsChecked.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeIsChecked(bool value)
        {
            return new ChangeCheckedCommand(this, value);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetID);
        }

        private sealed class ChangeCheckedCommand : IRecordCommand
        {
            private readonly WeakReference<CheckProperty> _property;
            private readonly bool _value;

            public ChangeCheckedCommand(CheckProperty property, bool value)
            {
                _property = new(property);
                _value = value;
            }

            public string Name => Strings.ChangeIsChecked;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _value;
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
                    target.Value = !_value;
                }
            }
        }
    }
}