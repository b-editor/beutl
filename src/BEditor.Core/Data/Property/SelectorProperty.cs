// SelectorProperty.cs
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
    /// Represents a property for selecting a single item from an array.
    /// </summary>
    [DebuggerDisplay("Index = {Index}, Item = {SelectItem}")]
    public class SelectorProperty : PropertyElement<SelectorPropertyMetadata>, IEasingProperty, IBindable<int>
    {
        internal static readonly PropertyChangedEventArgs _indexArgs = new(nameof(Index));
        private int _selectIndex;
        private List<IObserver<int>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<int>? _bindable;
        private Guid? _targetID;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public SelectorProperty(SelectorPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Index = metadata.DefaultIndex;
        }

        /// <summary>
        /// Get or set the selected item.
        /// </summary>
        public string? SelectItem
        {
            get => PropertyMetadata?.ItemSource[Index];
            set => Index = value is null ? 0 : (PropertyMetadata?.ItemSource?.IndexOf(value) ?? 0);
        }

        /// <summary>
        /// Gets or sets the index of the selected <see cref="SelectorPropertyMetadata.ItemSource"/>.
        /// </summary>
        public int Index
        {
            get => _selectIndex;
            set => SetValue(value, ref _selectIndex, _indexArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._selectIndex);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }

        /// <inheritdoc/>
        public int Value => Index;

        /// <inheritdoc/>
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        private List<IObserver<int>> Collection => _list ??= new();

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Value), Value);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Index = element.TryGetProperty(nameof(Value), out var value) ? value.GetInt32() : 0;
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
        }

        /// <summary>
        /// Create a command to change the selected item.
        /// </summary>
        /// <param name="index">New value for <see cref="Index"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeSelect(int index)
        {
            return new ChangeSelectCommand(this, index);
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<int> observer)
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
        public void OnNext(int value)
        {
            Index = value;
        }

        /// <inheritdoc/>
        public void Bind(IBindable<int>? bindable)
        {
            Index = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetID);
        }

        /// <summary>
        /// 選択されているアイテムを変更するコマンド.
        /// </summary>
        private sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly WeakReference<SelectorProperty> _property;
            private readonly int _new;
            private readonly int _old;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChangeSelectCommand"/> class.
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/>.</param>
            /// <param name="select">新しいインデックス.</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です.</exception>
            public ChangeSelectCommand(SelectorProperty property, int select)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = select;
                _old = property.Index;
            }

            public string Name => Strings.ChangeSelectItem;

            /// <inheritdoc/>
            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Index = _new;
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
                    target.Index = _old;
                }
            }
        }
    }
}