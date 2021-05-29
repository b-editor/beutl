// FontProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Drawing;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property for selecting a font.
    /// </summary>
    [DebuggerDisplay("Select = {Value}")]
    public class FontProperty : PropertyElement<FontPropertyMetadata>, IEasingProperty, IBindable<Font>
    {
        private Font _selectItem;
        private List<IObserver<Font>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<Font>? _bindable;
        private Guid? _targetID;

        /// <summary>
        /// Initializes a new instance of the <see cref="FontProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata for this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FontProperty(FontPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _selectItem = metadata.SelectItem;
        }

        /// <summary>
        /// Gets or sets the selected font.
        /// </summary>
        public Font Value
        {
            get => _selectItem;
            set => SetValue(value, ref _selectItem, DocumentProperty._valueArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._selectItem);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }

        /// <inheritdoc/>
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        private List<IObserver<Font>> Collection => _list ??= new();

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Value), Value.Filename);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            var filename = element.TryGetProperty(nameof(Value), out var value) ? value.GetString() : null;
            if (filename is not null)
            {
                Value = new(filename);
            }
            else
            {
                Value = FontManager.Default.LoadedFonts.First();
            }

            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
        }

        /// <summary>
        /// Create a command to change the font.
        /// </summary>
        /// <param name="font">New value for <see cref="Value"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeFont(Font font)
        {
            return new ChangeSelectCommand(this, font);
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<Font> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public void OnCompleted()
        {
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
        }

        /// <inheritdoc/>
        public void OnNext(Font value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public void Bind(IBindable<Font>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetID);
        }

        /// <summary>
        /// フォントを変更するコマンド.
        /// </summary>
        private sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly WeakReference<FontProperty> _property;
            private readonly Font _new;
            private readonly Font _old;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChangeSelectCommand"/> class.
            /// </summary>
            /// <param name="property">対象の <see cref="FontProperty"/>.</param>
            /// <param name="select">新しい値.</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です.</exception>
            public ChangeSelectCommand(FontProperty property, Font select)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = select;
                _old = property.Value;
            }

            public string Name => Strings.ChangeFont;

            /// <inheritdoc/>
            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _new;
                }
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
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