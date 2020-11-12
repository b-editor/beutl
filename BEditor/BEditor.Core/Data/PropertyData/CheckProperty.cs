using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// チェックボックスのプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class CheckProperty : PropertyElement, IEasingSetting, IObservable<bool>, INotifyPropertyChanged, IExtensibleDataObject {
        private bool isChecked;
        private List<IObserver<bool>> list;
        private List<IObserver<bool>> collection => list ??= new List<IObserver<bool>>();

        /// <summary>
        /// <see cref="CheckProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="CheckPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public CheckProperty(CheckPropertyMetadata metadata) {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            PropertyMetadata = metadata;
            isChecked = metadata.DefaultIsChecked;
        }

        /// <summary>
        /// チェックされている場合 <see langword="true"/>、そうでない場合は <see langword="false"/> となります
        /// </summary>
        [DataMember]
        public bool IsChecked { get => isChecked; set => SetValue(value, ref isChecked, nameof(IsChecked)); }

        private void CheckProperty_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(IsChecked)) {
                Parallel.For(0, collection.Count, i => {
                    var observer = collection[i];
                    try {
                        observer.OnNext(isChecked);
                        observer.OnCompleted();
                    }
                    catch (Exception ex) {
                        observer.OnError(ex);
                    }
                });
            }
        }
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<bool> observer) {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }
        /// <inheritdoc/>
        public override void PropertyLoaded() {
            base.PropertyLoaded();
            PropertyChanged += CheckProperty_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsChecked:{IsChecked} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// チェックされているかを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public class ChangeCheckedCommand : IUndoRedoCommand {
            private readonly CheckProperty CheckSetting;
            private readonly bool value;

            /// <summary>
            /// <see cref="ChangeCheckedCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="CheckProperty"/></param>
            /// <param name="value">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeCheckedCommand(CheckProperty property, bool value) {
                CheckSetting = property ?? throw new ArgumentNullException(nameof(property));
                this.value = value;
            }

            /// <inheritdoc/>
            public void Do() => CheckSetting.IsChecked = value;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => CheckSetting.IsChecked = !value;
        }
    }

    /// <summary>
    /// <see cref="CheckProperty"/> のメタデータを表します
    /// </summary>
    public record CheckPropertyMetadata(string Name, bool DefaultIsChecked = false) : PropertyElementMetadata(Name);
}
