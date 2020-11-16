using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.ObjectModel.EffectData;
using BEditor.Media;

namespace BEditor.ObjectModel.PropertyData
{
    /// <summary>
    /// フォントを選択するプロパティ表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class FontProperty : PropertyElement, IObservable<FontRecord>, IObserver<FontRecord>, INotifyPropertyChanged, IExtensibleDataObject, IChild<EffectElement>
    {
        private FontRecord selectItem;
        private List<IObserver<FontRecord>> list;
        private List<IObserver<FontRecord>> collection => list ??= new List<IObserver<FontRecord>>();

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


        /// <summary>
        /// 選択されているフォントを取得または設定します
        /// </summary>
        [DataMember]
        public FontRecord Select { get => selectItem; set => SetValue(value, ref selectItem, nameof(Select)); }

        private void FontProperty_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Select))
            {
                foreach (var observer in collection)
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
            }
        }
        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            PropertyChanged += FontProperty_PropertyChanged;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Select:{Select} Name:{PropertyMetadata?.Name})";
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<FontRecord> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
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


        #region Commands

        /// <summary>
        /// フォントを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeSelectCommand : IUndoRedoCommand
        {
            private readonly FontProperty Selector;
            private readonly FontRecord select;
            private readonly FontRecord oldselect;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FontProperty"/></param>
            /// <param name="select">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> または <paramref name="select"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(FontProperty property, FontRecord select)
            {
                Selector = property ?? throw new ArgumentNullException(nameof(property));
                this.select = select ?? throw new ArgumentNullException(nameof(select));
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

        #region StaticMember

        /// <summary>
        /// 読み込まれているフォントのリスト
        /// </summary>
        public static readonly List<FontRecord> FontList = new();

        /// <summary>
        /// フォントのスタイルのリスト
        /// </summary>
        public static readonly string[] FontStylesList = new string[]
        {
            Properties.Resources.FontStyle_Normal,
            Properties.Resources.FontStyle_Bold,
            Properties.Resources.FontStyle_Italic,
            Properties.Resources.FontStyle_UnderLine,
            Properties.Resources.FontStyle_StrikeThrough
        };

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
        public FontPropertyMetadata() : base(Properties.Resources.Font)
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
