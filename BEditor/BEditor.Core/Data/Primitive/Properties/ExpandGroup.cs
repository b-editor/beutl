using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// 複数の <see cref="PropertyElement"/> をエクスパンダーでまとめるクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class ExpandGroup : Group, IEasingProperty, IBindable<bool>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs isExpandedArgs = new(nameof(IsExpanded));
        private bool isOpen;
        private List<IObserver<bool>> list;

        private IDisposable BindDispose;

        #endregion


        /// <summary>
        /// <see cref="ExpandGroup"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="PropertyElementMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public ExpandGroup(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }


        private List<IObserver<bool>> collection => list ??= new();
        /// <summary>
        /// エクスパンダーが開いているかを取得または設定します
        /// </summary>
        [DataMember]
        public bool IsExpanded
        {
            get => isOpen;
            set => SetValue(value, ref isOpen, isExpandedArgs, () =>
            {
                foreach (var observer in collection)
                {
                    try
                    {
                        observer.OnNext(isOpen);
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
        [DataMember]
        public string BindHint { get; private set; }
        /// <inheritdoc/>
        public bool Value => IsExpanded;


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
        public override string ToString() => $"(IsExpanded:{IsExpanded} Name:{PropertyMetadata?.Name})";

        #region Ibindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(bool value)
        {
            IsExpanded = value;
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<bool> observer)
        {
            collection.Add(observer);
            return Disposable.Create(() => collection.Remove(observer));
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool> bindable)
        {
            BindDispose?.Dispose();

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                IsExpanded = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion
    }
}
