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
using BEditor.LangResources;

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
        private string _value = string.Empty;

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
        [Obsolete("Obsolete")]
        public string RawValue => _value ??= string.Empty;

        /// <summary>
        /// Gets or sets the name of the selected file.
        /// </summary>
        public string Value
        {
            get => _value ?? string.Empty;
            set
            {
                if (SetAndRaise(value, ref _value, DocumentProperty._valueArgs))
                {
                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(value);
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
            set => SetAndRaise(value, ref _mode, _modeArgs);
        }

        private List<IObserver<string>> Collection => _list ??= new();

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);

            if (Mode is FilePathType.FromProject)
            {
                writer.WriteString(nameof(Value), Path.GetRelativePath(this.GetRequiredParent<Project>().DirectoryName, Value));
            }
            else
            {
                writer.WriteString(nameof(Value), Value);
            }

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }
        }

        /// <inheritdoc/>
        public override void SetObjectData(DeserializeContext context)
        {
            base.SetObjectData(context);
            var element = context.Element;
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;

            if (element.TryGetProperty(nameof(Value), out var value))
            {
                var path = value.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                if (Path.IsPathRooted(path))
                {
                    Value = path;
                    Mode = FilePathType.FullPath;
                }
                else
                {
                    Value = Path.GetFullPath(path, this.GetRequiredParent<Project>().DirectoryName);
                    Mode = FilePathType.FromProject;
                }
            }
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