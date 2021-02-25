using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Drawing;
using System.Diagnostics;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property for selecting a font.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Select = {Select}")]
    public class FontProperty : PropertyElement<FontPropertyMetadata>, IEasingProperty, IBindable<Font>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _selectArgs = new(nameof(Select));
        private Font _selectItem;
        private List<IObserver<Font>>? _list;

        private IDisposable? _bindDispose;
        private IBindable<Font>? _bindable;
        private string? _bindHint;

        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="FontProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata for this property</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public FontProperty(FontPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _selectItem = metadata.SelectItem;
        }


        private List<IObserver<Font>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the selected font.
        /// </summary>
        [DataMember]
        public Font Select
        {
            get => _selectItem;
            set => SetValue(value, ref _selectItem, _selectArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._selectItem);
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
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }


        #region Methods

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_bindHint is not null && this.GetBindable(_bindHint, out var b))
            {
                Bind(b);
            }
            _bindHint = null;
        }

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
            _bindDispose?.Dispose();
            _bindable = bindable;

            if (bindable is not null)
            {
                Select = bindable.Value;

                // bindableが変更時にthisが変更
                _bindDispose = bindable.Subscribe(this);
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
    public record FontPropertyMetadata : PropertyElementMetadata, IPropertyBuilder<FontProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FontPropertyMetadata"/> class.
        /// </summary>
        public FontPropertyMetadata() : base(Resources.Font)
        {
            SelectItem = FontManager.Default.LoadedFonts.FirstOrDefault()!;
        }

        /// <summary>
        /// 
        /// </summary>
        public IEnumerable<Font> ItemSource => FontManager.Default.LoadedFonts;
        /// <summary>
        /// 
        /// </summary>
        public Font SelectItem { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath => "Name";

        /// <inheritdoc/>
        public FontProperty Build()
        {
            return new(this);
        }
    }
}
