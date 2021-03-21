using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property to select a folder.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Folder = {Value}")]
    public class FolderProperty : PropertyElement<FolderPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _modeArgs = new(nameof(Mode));
        private string _rawFolder = "";
        private List<IObserver<string>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private string? _bindHint;
        private FilePathType _mode;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="FolderProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FolderProperty(FolderPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Value = metadata.Default;
        }


        private List<IObserver<string>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the name of the selected folder.
        /// </summary>
        [DataMember(Name = "Folder")]
        public string RawValue
        {
            get => _rawFolder;
            private set => _rawFolder = value;
        }
        /// <summary>
        /// Gets or sets the name of the selected folder.
        /// </summary>
        public string Value
        {
            get
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is null) return _rawFolder;
                return (_mode is FilePathType.FromProject) ? Path.GetFullPath(_rawFolder, Parent.Parent.Parent.Parent.DirectoryName!) : _rawFolder;
            }
            set
            {
                if (value != Value)
                {
                    _rawFolder = GetFullPath(value);

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
                state.RawValue = state.GetPath();
            });
        }


        #region Methods

        private string GetPath()
        {
            if (Mode is FilePathType.FullPath)
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetFullPath(_rawFolder, Parent.Parent.Parent.Parent.DirectoryName!);
                }

                return _rawFolder;
            }
            else
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetRelativePath(Parent.Parent.Parent.Parent.DirectoryName!, Value);
                }

                return _rawFolder;
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

                return _rawFolder;
            }
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _bindHint);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Value), RawValue);
            writer.WriteString(nameof(BindHint), BindHint);
            writer.WriteNumber(nameof(Mode), (int)Mode);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? value.GetString() ?? "" : "";
            BindHint = element.TryGetProperty(nameof(BindHint), out var bind) ? bind.GetString() : null;
        }

        /// <summary>
        /// Create a command to rename a folder.
        /// </summary>
        /// <param name="path">New value for <see cref="Value"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeFolder(string path) => new ChangeFolderCommand(this, path);

        /// <inheritdoc/>
        public void OnCompleted() { }

        /// <inheritdoc/>
        public void OnError(Exception error) { }

        /// <inheritdoc/>
        public void OnNext(string value)
        {
            if (Directory.Exists(value))
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
        private sealed class ChangeFolderCommand : IRecordCommand
        {
            private readonly WeakReference<FolderProperty> _property;
            private readonly string _new;
            private readonly string _old;

            /// <summary>
            /// <see cref="ChangeFolderCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FolderProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFolderCommand(FolderProperty property, string path)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = path;
                _old = property.Value;
            }

            public string Name => CommandName.ChangeFolder;

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
    /// The metadata of <see cref="FolderProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="Default">The default value of <see cref="FolderProperty.Value"/>.</param>
    public record FolderPropertyMetadata(string Name, string Default = "") : PropertyElementMetadata(Name), IPropertyBuilder<FolderProperty>
    {
        /// <inheritdoc/>
        public FolderProperty Build()
        {
            return new(this);
        }
    }
}
