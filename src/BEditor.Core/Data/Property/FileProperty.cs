using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property to select a file.
    /// </summary>
    [DataContract]
    public class FileProperty : PropertyElement<FilePropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _fileArgs = new(nameof(File));
        private static readonly PropertyChangedEventArgs _modeArgs = new(nameof(Mode));
        private string _rawFile = "";
        private List<IObserver<string>>? _list;

        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private string? _bindHint;
        private FilePathType _mode;
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


        private List<IObserver<string>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the name of the selected file.
        /// </summary>
        [DataMember(Name = "File")]
        public string RawFile
        {
            get => _rawFile;
            private set => _rawFile = value;
        }
        /// <summary>
        /// Gets or sets the name of the selected file.
        /// </summary>
        public string File
        {
            get
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is null) return _rawFile;
                return (_mode is FilePathType.FromProject) ? Path.GetFullPath(_rawFile, Parent.Parent.Parent.Parent.DirectoryName!) : _rawFile;
            }
            set
            {
                if (value != File)
                {
                    _rawFile = GetFullPath(value);

                    RaisePropertyChanged(_fileArgs);

                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(File);
                        }
                        catch (Exception ex)
                        {
                            observer.OnError(ex);
                        }
                    }
                }
            }
        }
        /// <inheritdoc/>
        public string Value => File;
        /// <inheritdoc/>
        [DataMember]
        public string? BindHint
        {
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }
        /// <summary>
        /// Gets or sets the mode of the file path.
        /// </summary>
        [DataMember]
        public FilePathType Mode
        {
            get => _mode;
            set => SetValue(value, ref _mode, _modeArgs, this, state =>
            {
                state.RawFile = state.GetPath();
            });
        }


        #region Methods

        private string GetPath()
        {
            if (Mode is FilePathType.FullPath)
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetFullPath(_rawFile, Parent.Parent.Parent.Parent.DirectoryName!);
                }

                return _rawFile;
            }
            else
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetRelativePath(Parent.Parent.Parent.Parent.DirectoryName!, File);
                }

                return _rawFile;
            }
        }
        private string GetFullPath(string fullpath)
        {
            if (Mode is FilePathType.FullPath)
            {
                return fullpath;
            }
            else
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetRelativePath(Parent.Parent.Parent.Parent.DirectoryName!, fullpath);
                }

                return _rawFile;
            }
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_bindHint is not null && this.GetBindable(_bindHint, out var b))
            {
                Bind(b);
            }
            _bindHint = null;
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
            _bindDispose?.Dispose();
            _bindable = bindable;

            if (bindable is not null)
            {
                File = bindable.Value;

                // bindableが変更時にthisが変更
                _bindDispose = bindable.Subscribe(this);
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
