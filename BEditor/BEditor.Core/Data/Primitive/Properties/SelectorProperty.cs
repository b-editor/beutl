using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// 配列から一つのアイテムを選択するプロパティを表します
    /// </summary>
    [DataContract]
    public class SelectorProperty : PropertyElement<SelectorPropertyMetadata>, IEasingProperty, IBindable<int>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs indexArgs = new(nameof(Index));
        private int selectIndex;
        private List<IObserver<int>> list;

        private IDisposable BindDispose;
        private IBindable<int> Bindable;
        private string bindHint;

        #endregion


        /// <summary>
        /// <see cref="SelectorProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="SelectorPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public SelectorProperty(SelectorPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Index = metadata.DefaultIndex;
        }


        private List<IObserver<int>> Collection => list ??= new();
        /// <summary>
        /// 選択されているアイテムを取得します
        /// </summary>
        public object SelectItem
        {
            get => PropertyMetadata.ItemSource[Index];
            set => Index = PropertyMetadata.ItemSource.IndexOf(value);
        }

        /// <summary>
        /// 選択されている <see cref="SelectorPropertyMetadata.ItemSource"/> のインデックスを取得または設定します
        /// </summary>
        [DataMember]
        public int Index
        {
            get => selectIndex;
            set => SetValue(value, ref selectIndex, indexArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.selectIndex);
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
        public string BindHint
        {
            get => Bindable?.GetString();
            private set => bindHint = value;
        }


        #region Methods
        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();

            if (bindHint is not null && this.GetBindable(bindHint, out var b))
            {
                Bind(b);
            }
            bindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Index:{Index} Item:{SelectItem} Name:{PropertyMetadata?.Name})";

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

        public void Bind(IBindable<int>? bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
                Index = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion


        public static implicit operator int(SelectorProperty selector) => selector.Index;


        #region Commands

        /// <summary>
        /// 選択されているアイテムを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly SelectorProperty property;
            private readonly int @new;
            private readonly int old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/></param>
            /// <param name="select">新しいインデックス</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(SelectorProperty property, int select)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.@new = select;
                old = property.Index;
            }

            /// <inheritdoc/>
            public void Do() => property.Index = @new;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => property.Index = old;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="SelectorProperty"/> のメタデータを表します
    /// </summary>
    public record SelectorPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// <see cref="SelectorPropertyMetadata"/> の新しいインスタンスを初期化します
        /// </summary>
        public SelectorPropertyMetadata(string name, IList itemsource, int index = 0, string memberpath = "") : base(name)
        {
            DefaultIndex = index;
            ItemSource = itemsource;
            MemberPath = memberpath;
        }


        /// <summary>
        /// 
        /// </summary>
        public IList ItemSource { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public int DefaultIndex { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath { get; protected set; }
    }
}
