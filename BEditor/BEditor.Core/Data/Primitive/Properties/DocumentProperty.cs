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
    [DataContract]
    public class DocumentProperty : PropertyElement<DocumentPropertyMetadata>, IBindable<string>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs textArgs = new(nameof(Text));
        private string textProperty;
        private List<IObserver<string>> list;

        private IDisposable BindDispose;
        private IBindable<string> Bindable;
        private string bindHint;

        #endregion


        /// <summary>
        /// <see cref="DocumentProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="DocumentPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Text = metadata.DefaultText;
            HeightProperty = metadata.Height;
        }


        private List<IObserver<string>> Collection => list ??= new();
        /// <summary>
        /// 入力されている文字列を取得または設定します
        /// </summary>
        [DataMember]
        public string Text
        {
            get => textProperty;
            set => SetValue(value, ref textProperty, textArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.textProperty);
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
        public string BindHint
        {
            get => Bindable?.GetString();
            private set => bindHint = value;
        }


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
                Text = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        public override void Loaded()
        {
            if (IsLoaded) return;

            if (bindHint is not null && this.GetBindable(bindHint, out var b))
            {
                Bind(b);
            }
            bindHint = null;

            base.Loaded();
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
            private readonly DocumentProperty property;
            private readonly string @new;
            private readonly string old;

            /// <summary>
            /// <see cref="TextChangeCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property"></param>
            /// <param name="text"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public TextChangeCommand(DocumentProperty property, string text)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                @new = text;
                old = property.Text;
            }


            /// <inheritdoc/>
            public void Do() => property.Text = @new;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => property.Text = old;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Primitive.Properties.DocumentProperty"/> のメタデータを表します
    /// </summary>
    public record DocumentPropertyMetadata(string DefaultText, int? Height = null) : PropertyElementMetadata(string.Empty);
}
