using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// フォントを選択するプロパティ表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class FontProperty : PropertyElement<FontPropertyMetadata>, IEasingProperty, IBindable<FontRecord>
    {
        #region Fields

        /// <summary>
        /// 読み込まれているフォントのリスト
        /// </summary>
        public static readonly List<FontRecord> FontList = new();
        /// <summary>
        /// フォントのスタイルのリスト
        /// </summary>
        public static readonly string[] FontStylesList = new string[]
        {
            Resources.FontStyle_Normal,
            Resources.FontStyle_Bold,
            Resources.FontStyle_Italic,
            Resources.FontStyle_UnderLine,
            Resources.FontStyle_StrikeThrough
        };

        private static readonly PropertyChangedEventArgs selectArgs = new(nameof(Select));
        private FontRecord selectItem;
        private List<IObserver<FontRecord>> list;

        private IDisposable BindDispose;

        #endregion


        /// <summary>
        /// <see cref="FontProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="FontPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public FontProperty(FontPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            selectItem = metadata.SelectItem;
        }


        private List<IObserver<FontRecord>> Collection => list ??= new();
        /// <summary>
        /// 選択されているフォントを取得または設定します
        /// </summary>
        [DataMember]
        public FontRecord Select
        {
            get => selectItem;
            set => SetValue(value, ref selectItem, selectArgs, () =>
            {
                foreach (var observer in Collection)
                {
                    try
                    {
                        observer.OnNext(selectItem);
                        observer.OnCompleted();
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        public FontRecord Value { get; }
        /// <inheritdoc/>
        [DataMember]
        public string BindHint { get; private set; }


        #region Methods

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
        public override string ToString() => $"(Select:{Select} Name:{PropertyMetadata?.Name})";

        #region IBindable

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<FontRecord> observer)
        {
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(FontRecord value)
        {
            Select = value;
        }

        public void Bind(IBindable<FontRecord> bindable)
        {
            BindDispose?.Dispose();
            BindHint = null;

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                Select = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion


        #region Commands

        /// <summary>
        /// フォントを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeSelectCommand : IRecordCommand
        {
            private readonly FontProperty Selector;
            private readonly FontRecord select;
            private readonly FontRecord oldselect;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FontProperty"/></param>
            /// <param name="select">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(FontProperty property, FontRecord select)
            {
                Selector = property ?? throw new ArgumentNullException(nameof(property));
                this.select = select;
                oldselect = property.Select;
            }


            /// <inheritdoc/>
            public void Do() => Selector.Select = select;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Selector.Select = oldselect;
        }

        #endregion
    }

    /// <summary>
    /// <see cref="FontProperty"/> のメタデータを表します
    /// </summary>
    public record FontPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// <see cref="FontPropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public FontPropertyMetadata() : base(Resources.Font)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public IEnumerable<FontRecord> ItemSource => FontProperty.FontList;
        /// <summary>
        /// 
        /// </summary>
        public FontRecord SelectItem { get; init; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath => "Name";
    }
}
