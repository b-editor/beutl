using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Primitive.Properties
{
    [DataContract]
    public class FolderProperty : PropertyElement<FolderPropertyMetadata>, IEasingProperty, IBindable<string>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs folderArgs = new(nameof(Folder));
        private string folder;
        private List<IObserver<string>> list;

        private IDisposable BindDispose;
        private IBindable<string> Bindable;
        private string bindHint;

        #endregion

        /// <summary>
        /// <see cref="FolderProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="FolderPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public FolderProperty(FolderPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Folder = metadata.Default;
        }


        private List<IObserver<string>> Collection => list ??= new();
        /// <summary>
        /// フォルダの名前を取得または設定します
        /// </summary>
        [DataMember]
        public string Folder
        {
            get => folder;
            set => SetValue(value, ref folder, folderArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.folder);
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
        public override string ToString() => $"(Folder:{Folder} Name:{PropertyMetadata?.Name})";

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
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
                Folder = bindable.Value;

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
        public sealed class ChangeFolderCommand : IRecordCommand
        {
            private readonly FolderProperty property;
            private readonly string @new;
            private readonly string old;

            /// <summary>
            /// <see cref="ChangeFolderCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FolderProperty"/></param>
            /// <param name="path">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeFolderCommand(FolderProperty property, string path)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.@new = path;
                old = this.property.Folder;
            }


            /// <inheritdoc/>
            public void Do() => property.Folder = @new;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => property.Folder = old;
        }

        #endregion
    }

    public record FolderPropertyMetadata(string Name, string Default) : PropertyElementMetadata(Name);
}
