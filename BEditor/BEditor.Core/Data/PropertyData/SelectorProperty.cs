using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 配列から一つのアイテムを選択するプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class SelectorProperty : PropertyElement, IEasingSetting, IObservable<int>, INotifyPropertyChanged, IExtensibleDataObject {
        private int selectIndex;
        private List<IObserver<int>> list;
        private List<IObserver<int>> collection => list ??= new List<IObserver<int>>();

        /// <summary>
        /// <see cref="SelectorProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="SelectorPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public SelectorProperty(SelectorPropertyMetadata metadata) {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Index = metadata.DefaultIndex;
        }

        /// <summary>
        /// 選択されているアイテムを取得します
        /// </summary>
        public object SelectItem => (PropertyMetadata as SelectorPropertyMetadata).ItemSource[Index];

        /// <summary>
        /// 選択されている <see cref="SelectorPropertyMetadata.ItemSource"/> のインデックスを取得または設定します
        /// </summary>
        [DataMember]
        public int Index { get => selectIndex; set => SetValue(value, ref selectIndex, nameof(Index)); }


        public static implicit operator int(SelectorProperty selector) => selector.Index;
        private void SelectorProperty_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Index)) {
                Parallel.For(0, collection.Count, i => {
                    var observer = collection[i];
                    try {
                        observer.OnNext(selectIndex);
                        observer.OnCompleted();
                    }
                    catch (Exception ex) {
                        observer.OnError(ex);
                    }
                });
            }
        }
        /// <inheritdoc/>
        public override void PropertyLoaded() {
            base.PropertyLoaded();
            PropertyChanged += SelectorProperty_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Index:{Index} Item:{SelectItem} Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<int> observer) {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }


        #region Commands

        /// <summary>
        /// 選択されているアイテムを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeSelectCommand : IUndoRedoCommand {
            private readonly SelectorProperty Selector;
            private readonly int select;
            private readonly int oldselect;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="SelectorProperty"/></param>
            /// <param name="select">新しいインデックス</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(SelectorProperty property, int select) {
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
    public record SelectorPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// <see cref="SelectorPropertyMetadata"/> の新しいインスタンスを初期化します
        /// </summary>
        public SelectorPropertyMetadata(string name, IList itemsource, int index = 0, string memberpath = "") : base(name) {
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
