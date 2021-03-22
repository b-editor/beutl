using System;
using System.Collections;
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
    /// Represents a property for selecting a single item from an array.
    /// </summary>
    [DebuggerDisplay("Index = {Index}, Item = {SelectItem}")]
    public class SelectorProperty : PropertyElement<SelectorPropertyMetadata>, IEasingProperty, IBindable<int>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _indexArgs = new(nameof(Index));
        private int _selectIndex;
        private List<IObserver<int>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<int>? _bindable;
        private string? _bindHint;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public SelectorProperty(SelectorPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Index = metadata.DefaultIndex;
        }


        private List<IObserver<int>> Collection => _list ??= new();
        /// <summary>
        /// Get or set the selected item.
        /// </summary>
        public object? SelectItem
        {
            get => PropertyMetadata?.ItemSource[Index];
            set => Index = PropertyMetadata?.ItemSource?.IndexOf(value) ?? 0;
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
        public string? BindHint
        {
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }



        #region Methods
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _bindHint);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Value), Value);
            writer.WriteString(nameof(BindHint), BindHint);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Index = element.TryGetProperty(nameof(Value), out var value) ? value.GetInt32() : 0;
            BindHint = element.TryGetProperty(nameof(BindHint), out var bind) ? bind.GetString() : null;
        }

        /// <summary>
        /// Create a command to change the selected item.
        /// </summary>
        /// <param name="index">New value for <see cref="Index"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeSelect(int index) => new ChangeSelectCommand(this, index);

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<int> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public void OnCompleted() { }

        /// <inheritdoc/>
        public void OnError(Exception error) { }

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

        #endregion


        #region Commands

        /// <summary>
        /// 選択されているアイテムを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly WeakReference<SelectorProperty> _property;
            private readonly int _new;
            private readonly int _old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/></param>
            /// <param name="select">新しいインデックス</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(SelectorProperty property, int select)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = select;
                _old = property.Index;
            }

            public string Name => CommandName.ChangeSelectItem;

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

        #endregion
    }

    /// <summary>
    /// The metadata of <see cref="SelectorProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="ItemSource">The source of the item to be selected</param>
    /// <param name="DefaultIndex">The default value for <see cref="SelectorProperty.Index"/></param>
    /// <param name="MemberPath">The path to the member to display</param>
    public record SelectorPropertyMetadata(string Name, IList ItemSource, int DefaultIndex = 0, string MemberPath = "") : PropertyElementMetadata(Name), IPropertyBuilder<SelectorProperty>
    {
        /// <inheritdoc/>
        public SelectorProperty Build()
        {
            return new(this);
        }
    }
}
