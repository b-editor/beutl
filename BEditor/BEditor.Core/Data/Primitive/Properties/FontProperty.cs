using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Properties;
using BEditor.Drawing;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// フォントを選択するプロパティ表します
    /// </summary>
    [DataContract]
    public class FontProperty : PropertyElement<FontPropertyMetadata>, IEasingProperty, IBindable<Font>
    {
        #region Fields

        /// <summary>
        /// 読み込まれているフォントのリスト
        /// </summary>
        public static readonly List<Font> FontList = new();

        private static readonly PropertyChangedEventArgs selectArgs = new(nameof(Select));
        private Font selectItem;
        private List<IObserver<Font>> list;

        private IDisposable BindDispose;
        private IBindable<Font> Bindable;
        private string bindHint;

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


        private List<IObserver<Font>> Collection => list ??= new();
        /// <summary>
        /// 選択されているフォントを取得または設定します
        /// </summary>
        [DataMember]
        public Font Select
        {
            get => selectItem;
            set => SetValue(value, ref selectItem, selectArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.selectItem);
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
        public string BindHint
        {
            get => Bindable?.GetString();
            private set => bindHint = value;
        }


        #region Methods

        /// <inheritdoc/>
        public override void Loaded()
        {
            base.Loaded();

            if (bindHint is not null && this.GetBindable(bindHint, out var b))
            {
                Bind(b);
            }
            bindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(Select:{Select} Name:{PropertyMetadata?.Name})";

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

        public void Bind(IBindable<Font>? bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
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
            private readonly FontProperty property;
            private readonly Font @new;
            private readonly Font old;

            /// <summary>
            /// <see cref="ChangeSelectCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="FontProperty"/></param>
            /// <param name="select">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeSelectCommand(FontProperty property, Font select)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.@new = select;
                old = property.Select;
            }


            /// <inheritdoc/>
            public void Do() => property.Select = @new;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => property.Select = old;
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
            SelectItem = FontProperty.FontList.FirstOrDefault();
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
