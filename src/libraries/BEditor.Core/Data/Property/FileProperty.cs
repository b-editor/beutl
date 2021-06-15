// FileProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property to select a file.
    /// </summary>
    [DebuggerDisplay("File = {Value}")]
    public class FileProperty : PropertyElement<FilePropertyMetadata>, IEasingProperty, IBindable<string>
    {
        private static readonly PropertyChangedEventArgs _modeArgs = new(nameof(Mode));
        private List<IObserver<string>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<string>? _bindable;
        private Guid? _targetID;
        private FilePathType _mode;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FileProperty(FilePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Value = metadata.DefaultFile;
        }

        /// <summary>
        /// Gets the name of the selected file.
        /// </summary>
        public string RawValue { get; private set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the selected file.
        /// </summary>
        public string Value
        {
            get
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is null) return RawValue;
                return (_mode is FilePathType.FromProject) ? Path.GetFullPath(RawValue, Parent.Parent.Parent.Parent.DirectoryName!) : RawValue;
            }
            set
            {
                if (value != Value)
                {
                    RawValue = GetFullPath(value);

                    RaisePropertyChanged(DocumentProperty._valueArgs);
                    var value1 = Value;

                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(value1);
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
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        /// <summary>
        /// Gets or sets the mode of the file path.
        /// </summary>
        public FilePathType Mode
        {
            get => _mode;
            set
            {
                if (SetAndRaise(value, ref _mode, _modeArgs))
                {
                    RawValue = GetPath();
                }
            }
        }

        private List<IObserver<string>> Collection => _list ??= new();

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Value), RawValue);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }

            writer.WriteNumber(nameof(Mode), (int)Mode);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Value = element.TryGetProperty(nameof(Value), out var value) ? value.GetString() ?? string.Empty : string.Empty;
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
            Mode = element.TryGetProperty(nameof(Mode), out var mode) && mode.TryGetInt32(out var modei) ? (FilePathType)modei : FilePathType.FullPath;
        }

        /// <summary>
        /// Create a command to rename a file.
        /// </summary>
        /// <param name="path">New value for <see cref="File"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeFile(string path)
        {
            return new ChangeFileCommand(this, path);
        }

        /// <inheritdoc/>
        public void OnCompleted()
        {
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
        }

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

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetID);
        }

        private string GetPath()
        {
            if (Mode is FilePathType.FullPath)
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetFullPath(RawValue, Parent.Parent.Parent.Parent.DirectoryName!);
                }

                return RawValue;
            }
            else
            {
                if (Parent?.Parent?.Parent?.Parent?.DirectoryName is not null)
                {
                    return Path.GetRelativePath(Parent.Parent.Parent.Parent.DirectoryName!, Value);
                }

                return RawValue;
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

                return RawValue;
            }
        }

        /// <summary>
        /// ファイルの名前を変更するコマンド.
        /// </summary>
        private sealed class ChangeFileCommand : IRecordCommand
        {
            private readonly WeakReference<FileProperty> _property;
            private readonly string _new;
            private readonly string _old;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChangeFileCommand"/> class.
            /// </summary>
            /// <param name="property">対象の <see cref="FileProperty"/>.</param>
            /// <param name="path">新しい値.</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です.</exception>
            public ChangeFileCommand(FileProperty property, string path)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = path;
                _old = property.Value;
            }

            public string Name => Strings.ChangeFile;

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
    }
}