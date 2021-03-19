using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
    [DebuggerDisplay("File = {Value}")]
    public class FileProperty : PropertyElement<FilePropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
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
            Value = metadata.DefaultFile;
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
        public string Value
        {
            get
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is null) return _rawFile;
                return (_mode is FilePathType.FromProject) ? Path.GetFullPath(_rawFile, Parent.Parent.Parent.Parent.DirectoryName!) : _rawFile;
            }
            set
            {
                if (value != Value)
                {
                    _rawFile = GetFullPath(value);

                    RaisePropertyChanged(DocumentProperty._valueArgs);
                    var _value = Value;

                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(_value);
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
                    return Path.GetRelativePath(Parent.Parent.Parent.Parent.DirectoryName!, Value);
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
            this.AutoLoad(ref _bindHint);
        }

        /// <summary>
        /// Create a command to rename a file.
        /// </summary>
        /// <param name="path">New value for <see cref="File"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeFile(string path) => new ChangeFileCommand(this, path);

        /// <inheritdoc/>
        public void OnCompleted() { }

        /// <inheritdoc/>
        public void OnError(Exception error) { }

        /// <inheritdoc/>
        public void OnNext(string value)
        {
            if (File.Exists(value))
            {
                Value = value;
            }
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public void Bind(IBindable<string>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        #endregion


        #region Commands

        /// <summary>
        /// ファイルの名前を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeFileCommand : IRecordCommand
        {
            private readonly WeakReference<FileProperty> _property;
            private readonly string _new;
            private readonly string _old;

            /// <summary>
            /// <see cref="ChangeFileCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FileProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFileCommand(FileProperty property, string path)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = path;
                _old = property.Value;
            }

            public string Name => CommandName.ChangeFile;

            /// <inheritdoc/>
            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _new;
                }
            }
            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }
            /// <inheritdoc/>
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value = _old;
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// The metadata of <see cref="FileProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultFile">The default value of <see cref="FileProperty.Value"/></param>
    /// <param name="Filter">The filter for the file to be selected.</param>
    public record FilePropertyMetadata(string Name, string DefaultFile = "", FileFilter? Filter = null) : PropertyElementMetadata(Name), IPropertyBuilder<FileProperty>
    {
        /// <inheritdoc/>
        public FileProperty Build()
        {
            return new(this);
        }
    }
}
