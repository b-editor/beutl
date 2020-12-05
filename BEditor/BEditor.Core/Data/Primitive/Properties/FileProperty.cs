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
    public class FileProperty : PropertyElement<FilePropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs fileArgs = new(nameof(File));
        private string file;
        private List<IObserver<string>> list;

        private IDisposable BindDispose;
        private IBindable<string> Bindable;
        private string bindHint;

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


        private List<IObserver<string>> Collection => list ??= new();
        /// <summary>
        /// ファイルの名前を取得または設定します
        /// </summary>
        [DataMember]
        public string File
        {
            get => file;
            set => SetValue(value, ref file, fileArgs, () =>
            {
                foreach (var observer in Collection)
                {
                    try
                    {
                        observer.OnNext(File);
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public string Value => File;
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
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }

        /// <inheritdoc/>
        public void Bind(IBindable<string> bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
                File = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

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
    /// <see cref="BEditor.Core.Data.Primitive.Properties.FileProperty"/> のメタデータを表します
    /// </summary>
    public record FilePropertyMetadata(string Name, string DefaultFile = null, string Filter = null, string FilterName = null)
        : PropertyElementMetadata(Name);
}
