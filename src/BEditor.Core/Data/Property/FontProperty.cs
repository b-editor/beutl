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
    [DebuggerDisplay("Select = {Value}")]
    public class FontProperty : PropertyElement<FontPropertyMetadata>, IEasingProperty, IBindable<Font>
    {
        #region Fields
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
        public Font Value
        {
            get => _selectItem;
            set => SetValue(value, ref _selectItem, DocumentProperty._valueArgs, this, state =>
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
            this.AutoLoad(ref _bindHint);
        }

        /// <summary>
        /// Create a command to change the font.
        /// </summary>
        /// <param name="font">New value for <see cref="Value"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/></returns>
        [Pure]
        public IRecordCommand ChangeFont(Font font) => new ChangeSelectCommand(this, font);

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<Font> observer)
        {
            return BindingHelper.Subscribe(Collection, observer, Value);
        }

        /// <inheritdoc/>
        public void OnCompleted() { }

        /// <inheritdoc/>
        public void OnError(Exception error) { }

        /// <inheritdoc/>
        public void OnNext(Font value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public void Bind(IBindable<Font>? bindable)
        {
            Value = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        #endregion


        #region Commands

        /// <summary>
        /// フォントを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly WeakReference<FontProperty> _property;
            private readonly Font _new;
            private readonly Font _old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FontProperty"/></param>
            /// <param name="select">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(FontProperty property, Font select)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _new = select;
                _old = property.Value;
            }

            public string Name => CommandName.ChangeFont;

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

        #endregion
    }

    /// <summary>
    /// The metadata of <see cref="FontProperty"/>.
    /// </summary>
    public record FontPropertyMetadata : PropertyElementMetadata, IPropertyBuilder<FontProperty>
    {
        /// <summary>
        /// The metadata of <see cref="FontProperty"/>.
        /// </summary>
        public FontPropertyMetadata() : base(Resources.Font)
        {
            SelectItem = FontManager.Default.LoadedFonts.FirstOrDefault()!;
        }

        /// <summary>
        /// 
        /// </summary>
        public Font SelectItem { get; init; }

        /// <inheritdoc/>
        public FontProperty Build()
        {
            return new(this);
        }
    }
}
