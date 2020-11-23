using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// 複数行の文字のプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class DocumentProperty : PropertyElement, IBindable<string>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs textArgs = new(nameof(Text));
        private string textProperty;
        private List<IObserver<string>> list;

        private IDisposable BindDispose;

        #endregion


        /// <summary>
        /// <see cref="DocumentProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="DocumentPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            Text = metadata.DefaultText;
            HeightProperty = metadata.Height;
            PropertyMetadata = metadata;
        }


        private List<IObserver<string>> collection => list ??= new();
        /// <summary>
        /// 入力されている文字列を取得または設定します
        /// </summary>
        [DataMember]
        public string Text
        {
            get => textProperty;
            set => SetValue(value, ref textProperty, textArgs, () =>
            {
                foreach (var observer in collection)
                {
                    try
                    {
                        observer.OnNext(textProperty);
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <summary>
        /// 高さを取得または設定します
        /// </summary>
        public int? HeightProperty { get; set; }
        /// <inheritdoc/>
        public string Value => Text;
        /// <inheritdoc/>
        [DataMember]
        public string BindHint { get; private set; }


        #region Methods

        #region IBindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(string value)
        {
            Text = value;
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }
        /// <inheritdoc/>
        public void Bind(IBindable<string> bindable)
        {
            BindDispose?.Dispose();

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                Text = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            if (BindHint is not null && this.GetBindable(BindHint, out var b))
            {
                Bind(b);
            }
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";

        #endregion


        #region Commands

        /// <summary>
        /// 文字を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class TextChangeCommand : IRecordCommand
        {
            private readonly DocumentProperty Document;
            private readonly string newtext;
            private readonly string oldtext;

            /// <summary>
            /// <see cref="TextChangeCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property"></param>
            /// <param name="text"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public TextChangeCommand(DocumentProperty property, string text)
            {
                Document = property ?? throw new ArgumentNullException(nameof(property));
                newtext = text;
                oldtext = property.Text;
            }


            /// <inheritdoc/>
            public void Do() => Document.Text = newtext;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Document.Text = oldtext;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="DocumentProperty"/> のメタデータを表します
    /// </summary>
    public record DocumentPropertyMetadata(string DefaultText, int? Height = null) : PropertyElementMetadata(string.Empty);
}
