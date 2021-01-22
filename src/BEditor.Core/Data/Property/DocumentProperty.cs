using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// 複数行の文字のプロパティを表します
    /// </summary>
    [DataContract]
    public class DocumentProperty : PropertyElement<DocumentPropertyMetadata>, IBindable<string>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _TextArgs = new(nameof(Text));
        private string _Text = "";
        private List<IObserver<string>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<string>? _Bindable;
        private string? _BindHint;
        #endregion


        private List<IObserver<string>> Collection => _List ??= new();
        /// <summary>
        /// 入力されている文字列を取得または設定します
        /// </summary>
        [DataMember]
        public string Text
        {
            get => _Text;
            set => SetValue(value, ref _Text, _TextArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._Text);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public string Value => Text;
        /// <inheritdoc/>
        [DataMember]
        public string? BindHint
        {
            get => _Bindable?.GetString();
            private set => _BindHint = value;
        }


        /// <summary>
        /// <see cref="DocumentProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="DocumentPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Text = metadata.DefaultText;
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
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                Text = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

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
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";

        #endregion


        #region Commands

        /// <summary>
        /// 文字を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class TextChangeCommand : IRecordCommand
        {
            private readonly DocumentProperty _Property;
            private readonly string _New;
            private readonly string _Old;

            /// <summary>
            /// <see cref="TextChangeCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property"></param>
            /// <param name="text"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public TextChangeCommand(DocumentProperty property, string text)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = text;
                _Old = property.Text;
            }

            public string Name => CommandName.ChangeText;

            /// <inheritdoc/>
            public void Do() => _Property.Text = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.Text = _Old;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Property.DocumentProperty"/> のメタデータを表します
    /// </summary>
    public record DocumentPropertyMetadata(string DefaultText, int? Height = null) : PropertyElementMetadata(string.Empty);
}
