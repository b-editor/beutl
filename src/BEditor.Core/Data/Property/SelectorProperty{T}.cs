// SelectorProperty{T}.cs
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
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property for selecting a single item from an array.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    [DebuggerDisplay("Index = {Index}, Item = {SelectItem}")]
    public class SelectorProperty<T> : PropertyElement<SelectorPropertyMetadata<T>>, IEasingProperty, IBindable<T?>
        where T : IJsonObject, IEquatable<T>
    {
        private static readonly PropertyChangedEventArgs _selectItemArgs = new(nameof(SelectItem));
        private T? _selectItem;
        private List<IObserver<T?>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<T?>? _bindable;
        private Guid? _targetID;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorProperty{T}"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public SelectorProperty(SelectorPropertyMetadata<T> metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            // 内部で indexer 呼び出し
            _selectItem = metadata.ItemSource.ElementAtOrDefault(metadata.DefaultIndex);
        }

        /// <summary>
        /// Gets or sets the selected item.
        /// </summary>
        public T? SelectItem
        {
            get => _selectItem;
            set => SetValue(value, ref _selectItem, _selectItemArgs, this, state =>
            {
                state.RaisePropertyChanged(SelectorProperty._indexArgs);
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

        /// <summary>
        /// Gets the index of the selected <see cref="SelectorPropertyMetadata{T}.ItemSource"/>.
        /// </summary>
        public int Index
        {
            get
            {
                if (SelectItem is null) return -1;

                return PropertyMetadata?.ItemSource?.IndexOf(SelectItem) ?? -1;
            }
        }

        /// <inheritdoc/>
        public T? Value => SelectItem;

        /// <inheritdoc/>
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        private List<IObserver<T?>> Collection => _list ??= new();

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);

            writer.WritePropertyName(nameof(Value));
            SelectItem?.GetObjectData(writer);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);

            SelectItem = (T)FormatterServices.GetUninitializedObject(typeof(T));
            SelectItem.SetObjectData(element.GetProperty(nameof(Value)));
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
            return new ChangeSelectCommand(
                this,
                PropertyMetadata is null ? default : PropertyMetadata.ItemSource.ElementAtOrDefault(index));
        }

        /// <summary>
        /// Create a command to change the selected item.
        /// </summary>
        /// <param name="value">New value for <see cref="SelectItem"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeSelect(T? value)
        {
            return new ChangeSelectCommand(this, value);
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<T?> observer)
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
        public void OnNext(T? value)
        {
            SelectItem = value;
        }

        /// <inheritdoc/>
        public void Bind(IBindable<T?>? bindable)
        {
            SelectItem = this.Bind(bindable, out _bindable, ref _bindDispose);
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
            private readonly WeakReference<SelectorProperty<T>> _property;
            private readonly T? _new;
            private readonly T? _old;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChangeSelectCommand"/> class.
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/>.</param>
            /// <param name="select">新しいインデックス.</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です.</exception>
            public ChangeSelectCommand(SelectorProperty<T> property, T? select)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = select;
                _old = property.SelectItem;
            }

            public string Name => Strings.ChangeSelectItem;

            /// <inheritdoc/>
            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.SelectItem = _new;
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
                    target.SelectItem = _old;
                }
            }
        }
    }
}