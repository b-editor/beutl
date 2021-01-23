using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Service;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// ファイルを選択するプロパティを表します
    /// </summary>
    [DataContract]
    public class FileProperty : PropertyElement<FilePropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _FileArgs = new(nameof(File));
        private string _File = "";
        private List<IObserver<string>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<string>? _Bindable;
        private string? _BindHint;
        #endregion


        private List<IObserver<string>> Collection => _List ??= new();
        /// <summary>
        /// ファイルの名前を取得または設定します
        /// </summary>
        [DataMember]
        public string File
        {
            get => _File;
            set => SetValue(value, ref _File, _FileArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.File);
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
        public string? BindHint
        {
            get => _Bindable?.GetString();
            private set => _BindHint = value;
        }


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
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }

        /// <inheritdoc/>
        public void Bind(IBindable<string>? bindable)
        {
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                File = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
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
            private readonly FileProperty _Property;
            private readonly string _New;
            private readonly string _Old;

            /// <summary>
            /// <see cref="ChangeFileCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FileProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFileCommand(FileProperty property, string path)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = path;
                _Old = _Property.File;
            }

            public string Name => CommandName.ChangeFile;

            /// <inheritdoc/>
            public void Do() => _Property.File = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.File = _Old;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Property.FileProperty"/> のメタデータを表します
    /// </summary>
    public record FilePropertyMetadata(
        string Name,
        string DefaultFile = "",
        FileFilter? Filter = null) : PropertyElementMetadata(Name);
}
