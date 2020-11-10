using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 複数の <see cref="PropertyElement"/> をエクスパンダーでまとめるクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class ExpandGroup : Group, IEasingSetting, IObservable<bool>, INotifyPropertyChanged, IExtensibleDataObject {
        private bool isOpen;
        private List<IObserver<bool>> list;
        private List<IObserver<bool>> collection => list ??= new List<IObserver<bool>>();

        /// <summary>
        /// エクスパンダーが開いているかを取得または設定します
        /// </summary>
        [DataMember]
        public bool IsExpanded { get => isOpen; set => SetValue(value, ref isOpen, nameof(IsExpanded)); }

        /// <summary>
        /// <see cref="ExpandGroup"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="PropertyElementMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public ExpandGroup(PropertyElementMetadata metadata) {
            PropertyMetadata = metadata??throw new ArgumentNullException(nameof(metadata));
        }

        private void ExpandGroup_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(IsExpanded)) {
                Parallel.For(0, collection.Count, i => {
                    var observer = collection[i];
                    try {
                        observer.OnNext(isOpen);
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
            PropertyChanged += ExpandGroup_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsExpanded:{IsExpanded} Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<bool> observer) {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }
    }
}
