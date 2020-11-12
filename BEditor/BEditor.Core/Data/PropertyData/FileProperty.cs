using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// ファイルを選択するプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class FileProperty : PropertyElement, IEasingSetting, IObservable<string>, INotifyPropertyChanged, IExtensibleDataObject {
        private string file;
        private List<IObserver<string>> list;
        private List<IObserver<string>> collection => list ??= new List<IObserver<string>>();

        /// <summary>
        /// <see cref="FileProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="FilePropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public FileProperty(FilePropertyMetadata metadata) {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            File = metadata.DefaultFile;
        }

        /// <summary>
        /// ファイルの名前を取得または設定します
        /// </summary>
        [DataMember]
        public string File { get => file; set => SetValue(value, ref file, nameof(File)); }

        private void FileProperty_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(File)) {
                Parallel.For(0, collection.Count, i => {
                    var observer = collection[i];
                    try {
                        observer.OnNext(file);
                        observer.OnCompleted();
                    }
                    catch(Exception ex) {
                        observer.OnError(ex);
                    }
                });
            }
        }
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer) {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }
        /// <inheritdoc/>
        public override void PropertyLoaded() {
            base.PropertyLoaded();
            PropertyChanged += FileProperty_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(File:{File} Name:{PropertyMetadata?.Name})";


        #region Commands

        /// <summary>
        /// ファイルの名前を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public class ChangeFileCommand : IUndoRedoCommand {
            private readonly FileProperty FileSetting;
            private readonly string path;
            private readonly string oldpath;

            /// <summary>
            /// <see cref="ChangeFileCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FileProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFileCommand(FileProperty property, string path) {
                FileSetting = property ?? throw new ArgumentNullException(nameof(property));
                this.path = path;
                oldpath = FileSetting.File;
            }


            /// <inheritdoc/>
            public void Do() => FileSetting.File = path;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => FileSetting.File = oldpath;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="FileProperty"/> のメタデータを表します
    /// </summary>
    public record FilePropertyMetadata(string Name, string DefaultFile = null, string Filter = null, string FilterName = null)
        : PropertyElementMetadata(Name);
}
