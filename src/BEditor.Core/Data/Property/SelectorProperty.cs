using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property for selecting a single item from an array.
    /// </summary>
    [DataContract]
    public class SelectorProperty : PropertyElement<SelectorPropertyMetadata>, IEasingProperty, IBindable<int>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _IndexArgs = new(nameof(Index));
        private int _SelectIndex;
        private List<IObserver<int>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<int>? _Bindable;
        private string? _BindHint;
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

        private List<IObserver<int>> Collection => _List ??= new();
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
        [DataMember]
        public int Index
        {
            get => _SelectIndex;
            set => SetValue(value, ref _SelectIndex, _IndexArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._SelectIndex);
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
        [DataMember]
        public string? BindHint
        {
            get => _Bindable?.GetString();
            private set => _BindHint = value;
        }



        #region Methods
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_BindHint is not null && this.GetBindable(_BindHint, out var b))
            {
                Bind(b);
            }
            _BindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Index:{Index} Item:{SelectItem} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// Create a command to change the selected item.
        /// </summary>
        /// <param name="index">New value for <see cref="Index"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeSelect(int index) => new ChangeSelectCommand(this, index);

        #region IBindable

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<int> observer)
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
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                Index = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion


        #region Commands

        /// <summary>
        /// 選択されているアイテムを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly SelectorProperty _Property;
            private readonly int _New;
            private readonly int _Old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/></param>
            /// <param name="select">新しいインデックス</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(SelectorProperty property, int select)
            {
                this._Property = property ?? throw new ArgumentNullException(nameof(property));
                this._New = select;
                _Old = property.Index;
            }

            public string Name => CommandName.ChangeSelectItem;

            /// <inheritdoc/>
            public void Do() => _Property.Index = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.Index = _Old;
        }

        #endregion
    }

    /// <summary>
    /// Represents the metadata of a <see cref="SelectorProperty"/>.
    /// </summary>
    public record SelectorPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectorPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="ItemSource">Source of the item to be selected</param>
        /// <param name="DefaultIndex">Default value for <see cref="SelectorProperty.Index"/></param>
        /// <param name="MemberPath">Path to the member to display</param>
        public SelectorPropertyMetadata(string Name, IList ItemSource, int DefaultIndex = 0, string MemberPath = "") : base(Name)
        {
            this.DefaultIndex = DefaultIndex;
            this.ItemSource = ItemSource;
            this.MemberPath = MemberPath;
        }


        /// <summary>
        /// Get the source of the item to be selected.
        /// </summary>
        public IList ItemSource { get; protected set; }
        /// <summary>
        /// Get the default value of <see cref="SelectorProperty.Index"/>.
        /// </summary>
        public int DefaultIndex { get; protected set; }
        /// <summary>
        /// Get the path to the member to display.
        /// </summary>
        public string MemberPath { get; protected set; }
    }
}
