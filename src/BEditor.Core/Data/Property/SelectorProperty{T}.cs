using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// 配列から一つのアイテムを選択するプロパティを表します
    /// </summary>
    [DataContract]
    public class SelectorProperty<T> : PropertyElement<SelectorPropertyMetadata<T?>>, IEasingProperty, IBindable<T?>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _SelectItemArgs = new(nameof(SelectItem));
        private T? _SelectItem;
        private List<IObserver<T?>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<T?>? _Bindable;
        private string? _BindHint;
        #endregion


        /// <summary>
        /// <see cref="SelectorProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="SelectorPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public SelectorProperty(SelectorPropertyMetadata<T?> metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _SelectItem = metadata.DefaultItem;
        }

        private List<IObserver<T?>> Collection => _List ??= new();
        /// <summary>
        /// 選択されているアイテムを取得します
        /// </summary>
        [DataMember]
        public T? SelectItem
        {
            get => _SelectItem;
            set => SetValue(value, ref _SelectItem, _SelectItemArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._SelectItem);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }

        /// <summary>
        /// 選択されている <see cref="SelectorPropertyMetadata.ItemSource"/> のインデックスを取得または設定します
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
        /// 選択されているアイテムを変更するコマンドを作成します
        /// </summary>
        [Pure]
        public IRecordCommand ChangeSelect(T? value) => new ChangeSelectCommand(this, value);

        #region IBindable

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<T?> observer)
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
        public void OnNext(T? value)
        {
            SelectItem = value;
        }

        public void Bind(IBindable<T?>? bindable)
        {
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                SelectItem = bindable.Value;

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
            private readonly SelectorProperty<T> _Property;
            private readonly T? _New;
            private readonly T? _Old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/></param>
            /// <param name="select">新しいインデックス</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(SelectorProperty<T> property, T? select)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = select;
                _Old = property.SelectItem;
            }

            public string Name => CommandName.ChangeSelectItem;

            /// <inheritdoc/>
            public void Do() => _Property.SelectItem = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.SelectItem = _Old;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="SelectorProperty{T}"/> のメタデータを表します
    /// </summary>
    public record SelectorPropertyMetadata<T> : PropertyElementMetadata
    {
        /// <summary>
        /// <see cref="SelectorPropertyMetadata"/> の新しいインスタンスを初期化します
        /// </summary>
        public SelectorPropertyMetadata(string name, IList<T> itemsource, T? defaultitem = default, string memberpath = "") : base(name)
        {
            DefaultItem = defaultitem ?? itemsource.FirstOrDefault();
            ItemSource = itemsource;
            MemberPath = memberpath;
        }


        /// <summary>
        /// 
        /// </summary>
        public IList<T> ItemSource { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public T? DefaultItem { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath { get; protected set; }
    }
}
