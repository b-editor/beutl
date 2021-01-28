using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
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
    /// Represents a property to select a file.
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


        /// <summary>
        /// Initializes a new instance of the <see cref="FileProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FileProperty(FilePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            File = metadata.DefaultFile;
        }


        private List<IObserver<string>> Collection => _List ??= new();
        /// <summary>
        /// Gets or sets the name of the selected file.
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

        /// <summary>
        /// Create a command to rename a file.
        /// </summary>
        /// <param name="path">New value for <see cref="File"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeFile(string path) => new ChangeFileCommand(this, path);

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
        private sealed class ChangeFileCommand : IRecordCommand
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
    /// Represents the metadata of a <see cref="FileProperty"/>.
    /// </summary>
    public record FilePropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FilePropertyMetadata"/>
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultFile">Default value of <see cref="FileProperty.File"/></param>
        /// <param name="Filter">Filter the files to select</param>
        public FilePropertyMetadata(string Name, string DefaultFile = "", FileFilter? Filter = null) : base(Name)
        {
            this.DefaultFile = DefaultFile;
            this.Filter = Filter;
        }

        /// <summary>
        /// Get the default value of <see cref="FileProperty.File"/>.
        /// </summary>
        public string DefaultFile { get; init; }
        /// <summary>
        /// Get the filter for the file to be selected.
        /// </summary>
        public FileFilter? Filter { get; init; }
    }
}
