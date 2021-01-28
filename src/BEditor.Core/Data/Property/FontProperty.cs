using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Drawing;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a property for selecting a font.
    /// </summary>
    [DataContract]
    public class FontProperty : PropertyElement<FontPropertyMetadata>, IEasingProperty, IBindable<Font>
    {
        #region Fields

        /// <summary>
        /// 読み込まれているフォントのリスト
        /// </summary>
        //Todo: FontManagerを作る
        public static readonly List<Font> FontList = new();

        private static readonly PropertyChangedEventArgs _SelectArgs = new(nameof(Select));
        private Font _SelectItem;
        private List<IObserver<Font>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<Font>? _Bindable;
        private string? _BindHint;

        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="FontProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata for this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FontProperty(FontPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _SelectItem = metadata.SelectItem;
        }


        private List<IObserver<Font>> Collection => _List ??= new();
        /// <summary>
        /// Gets or sets the selected font.
        /// </summary>
        [DataMember]
        public Font Select
        {
            get => _SelectItem;
            set => SetValue(value, ref _SelectItem, _SelectArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._SelectItem);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public Font Value => Select;
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
        public override string ToString() => $"(Select:{Select} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// Create a command to change the font.
        /// </summary>
        /// <param name="font">New value for <see cref="Select"/></param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeFont(Font font) => new ChangeSelectCommand(this, font);

        #region IBindable

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<Font> observer)
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
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(Font value)
        {
            Select = value;
        }

        /// <inheritdoc/>
        public void Bind(IBindable<Font>? bindable)
        {
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                Select = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion


        #region Commands

        /// <summary>
        /// フォントを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly FontProperty _Property;
            private readonly Font _New;
            private readonly Font _Old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FontProperty"/></param>
            /// <param name="select">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(FontProperty property, Font select)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _New = select;
                _Old = property.Select;
            }

            public string Name => CommandName.ChangeFont;

            /// <inheritdoc/>
            public void Do() => _Property.Select = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.Select = _Old;
        }

        #endregion
    }

    /// <summary>
    /// Represents the metadata of a <see cref="FontProperty"/>.
    /// </summary>
    public record FontPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FontPropertyMetadata"/> class.
        /// </summary>
        public FontPropertyMetadata() : base(Resources.Font)
        {
            SelectItem = FontProperty.FontList.FirstOrDefault()!;
        }

        /// <summary>
        /// 
        /// </summary>
        public IEnumerable<Font> ItemSource => FontProperty.FontList;
        /// <summary>
        /// 
        /// </summary>
        public Font SelectItem { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath => "Name";
    }
}
