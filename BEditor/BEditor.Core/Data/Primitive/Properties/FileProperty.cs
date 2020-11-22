using System;
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
    /// ファイルを選択するプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class FileProperty : PropertyElement, IEasingProperty, IBindable<string>
    {
        #region フィールド

        private static readonly PropertyChangedEventArgs fileArgs = new(nameof(File));
        private string file;
        private List<IObserver<string>> list;

        private IDisposable BindDispose;

        #endregion


        /// <summary>
        /// <see cref="FileProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="FilePropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public FileProperty(FilePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            File = metadata.DefaultFile;
        }

        private List<IObserver<string>> collection => list ??= new();
        /// <summary>
        /// ファイルの名前を取得または設定します
        /// </summary>
        [DataMember]
        public string File
        {
            get => file;
            set => SetValue(value, ref file, fileArgs, FileProperty_PropertyChanged);
        }
        /// <inheritdoc/>
        public string Value => File;
        /// <inheritdoc/>
        [DataMember]
        public string BindHint { get; private set; }

        private void FileProperty_PropertyChanged()
        {
            foreach (var observer in collection)
            {
                try
                {
                    observer.OnNext(file);
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }

        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();

            if (BindHint is not null && this.GetBindable(BindHint, out var b))
            {
                Bind(b);
            }
        }
        /// <inheritdoc/>
        public override string ToString() => $"(File:{File} Name:{PropertyMetadata?.Name})";

        #region Ibindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(string value)
        {
            if (System.IO.File.Exists(value))
                File = value;
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }

        /// <inheritdoc/>
        public void Bind(IBindable<string> bindable)
        {
            BindDispose?.Dispose();

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                File = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion


        #region Commands

        /// <summary>
        /// ファイルの名前を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeFileCommand : IRecordCommand
        {
            private readonly FileProperty FileSetting;
            private readonly string path;
            private readonly string oldpath;

            /// <summary>
            /// <see cref="ChangeFileCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FileProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFileCommand(FileProperty property, string path)
            {
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
