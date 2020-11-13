using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace BEditor.Core.Data.PropertyData
{
    /// <summary>
    /// 複数行の文字のプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class DocumentProperty : PropertyElement, IObservable<string>
    {
        private string textProperty;
        private List<IObserver<string>> list;
        private List<IObserver<string>> collection => list ??= new List<IObserver<string>>();

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


        /// <summary>
        /// 入力されている文字列を取得または設定します
        /// </summary>
        [DataMember]
        public string Text { get => textProperty; set => SetValue(value, ref textProperty, nameof(Text)); }
        /// <summary>
        /// 高さを取得または設定します
        /// </summary>
        public int? HeightProperty { get; set; }

        private void DocumentProperty_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Text))
            {
                Parallel.For(0, collection.Count, i =>
                {
                    var observer = collection[i];
                    try
                    {
                        observer.OnNext(textProperty);
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                });
            }
        }
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }
        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            PropertyChanged += DocumentProperty_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";

        #region Commands

        /// <summary>
        /// 文字を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class TextChangeCommand : IUndoRedoCommand
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
