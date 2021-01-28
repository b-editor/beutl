using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a property to select a folder.
    /// </summary>
    [DataContract]
    public class FolderProperty : PropertyElement<FolderPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _FolderArgs = new(nameof(Folder));
        private string _Folder = "";
        private List<IObserver<string>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<string>? _Bindable;
        private string? _BindHint;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="FolderProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FolderProperty(FolderPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Folder = metadata.Default;
        }


        private List<IObserver<string>> Collection => _List ??= new();
        /// <summary>
        /// Gets or sets the name of the selected folder.
        /// </summary>
        [DataMember]
        public string Folder
        {
            get => _Folder;
            set => SetValue(value, ref _Folder, _FolderArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._Folder);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public string Value => Folder;
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
        public override string ToString() => $"(Folder:{Folder} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// Create a command to rename a folder.
        /// </summary>
        /// <param name="path">New value for <see cref="Folder"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeFolder(string path) => new ChangeFolderCommand(this, path);

        #region Ibindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(string value)
        {
            if (System.IO.File.Exists(value))
                Folder = value;
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
                Folder = bindable.Value;

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
        private sealed class ChangeFolderCommand : IRecordCommand
        {
            private readonly FolderProperty _Property;
            private readonly string _New;
            private readonly string _Old;

            /// <summary>
            /// <see cref="ChangeFolderCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FolderProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFolderCommand(FolderProperty property, string path)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = path;
                _Old = _Property.Folder;
            }

            public string Name => CommandName.ChangeFolder;

            /// <inheritdoc/>
            public void Do() => _Property.Folder = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.Folder = _Old;
        }

        #endregion
    }

    /// <summary>
    /// Represents the metadata of a <see cref="FolderProperty"/>.
    /// </summary>
    public record FolderPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FolderPropertyMetadata"/>
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="Default">Default value of <see cref="FolderProperty.Folder"/></param>
        public FolderPropertyMetadata(string Name, string Default = "") : base(Name)
        {
            this.Default = Default;
        }

        /// <summary>
        /// Get the default value of <see cref="FolderProperty.Folder"/>.
        /// </summary>
        public string Default { get; init; }
    }
}
