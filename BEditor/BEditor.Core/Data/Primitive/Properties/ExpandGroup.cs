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
    [DataContract]
    public abstract class ExpandGroup : Group, IEasingProperty, IBindable<bool>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs isExpandedArgs = new(nameof(IsExpanded));
        private bool isOpen;
        private List<IObserver<bool>> list;

        private IDisposable BindDispose;
        private IBindable<bool> Bindable;
        private string bindHint;

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


        private List<IObserver<bool>> Collection => list ??= new();
        /// <summary>
        /// エクスパンダーが開いているかを取得または設定します
        /// </summary>
        [DataMember]
        public bool IsExpanded
        {
            get => isOpen;
            set => SetValue(value, ref isOpen, isExpandedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.isOpen);
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
        public string BindHint
        {
            get => Bindable?.GetString();
            private set => bindHint = value;
        }
        /// <inheritdoc/>
        public bool Value => IsExpanded;


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
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
                IsExpanded = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion
    }
}
