using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.ObjectModel.EffectData;
using BEditor.ObjectModel.PropertyData.EasingSetting;

namespace BEditor.ObjectModel.PropertyData.Generic
{
    /// <summary>
    /// 配列から一つのアイテムを選択するプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class SelectorProperty<T> : PropertyElement, IEasingSetting, IObservable<T>, IObserver<T>, INotifyPropertyChanged, IExtensibleDataObject, IChild<EffectElement>
    {
        private int selectIndex;
        private List<IObserver<T>> list;
        private List<IObserver<T>> collection => list ??= new();

        /// <summary>
        /// <see cref="SelectorProperty{T}"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="SelectorPropertyMetadata{T}"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public SelectorProperty(SelectorPropertyMetadata<T> metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Index = metadata.DefaultIndex;
        }

        /// <summary>
        /// 選択されているアイテムを取得します
        /// </summary>
        public T SelectItem => (PropertyMetadata as SelectorPropertyMetadata<T>).ItemSource[Index];

        /// <summary>
        /// 選択されている <see cref="SelectorPropertyMetadata{T}.ItemSource"/> のインデックスを取得または設定します
        /// </summary>
        [DataMember]
        public int Index
        {
            get => selectIndex;
            set => SetValue(value, ref selectIndex, nameof(Index));
        }

        private void SelectorProperty_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Index))
            {
                foreach (var observer in collection)
                {
                    try
                    {
                        observer.OnNext(SelectItem);
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            }
        }
        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            PropertyChanged += SelectorProperty_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Index:{Index} Item:{SelectItem} Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<T> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(T value)
        {
            Index = (PropertyMetadata as SelectorPropertyMetadata<T>).ItemSource.IndexOf(value);
        }


        #region Commands

        /// <summary>
        /// 選択されているアイテムを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeSelectCommand : IUndoRedoCommand
        {
            private readonly SelectorProperty<T> Selector;
            private readonly int select;
            private readonly int oldselect;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/></param>
            /// <param name="select">新しいインデックス</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(SelectorProperty<T> property, int select)
            {
                Selector = property ?? throw new ArgumentNullException(nameof(property));
                this.select = select;
                oldselect = property.Index;
            }

            /// <inheritdoc/>
            public void Do() => Selector.Index = select;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Selector.Index = oldselect;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="SelectorProperty"/> のメタデータを表します
    /// </summary>
    public record SelectorPropertyMetadata<T> : SelectorPropertyMetadata
    {
        /// <summary>
        /// <see cref="SelectorPropertyMetadata"/> の新しいインスタンスを初期化します
        /// </summary>
        public SelectorPropertyMetadata(string name, IList<T> itemsource, int index = 0, string memberpath = "") : base(name, null, index, memberpath)
        {
            ItemSource = itemsource;
        }


        /// <summary>
        /// 
        /// </summary>
        public new IList<T> ItemSource { get; protected set; }
    }
}
