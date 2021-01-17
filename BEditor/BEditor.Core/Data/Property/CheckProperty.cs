using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// チェックボックスのプロパティを表します
    /// </summary>
    [DataContract]
    public class CheckProperty : PropertyElement<CheckPropertyMetadata>, IEasingProperty, IBindable<bool>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs checkedArgs = new(nameof(IsChecked));
        private bool isChecked;
        private List<IObserver<bool>> list;

        private IDisposable BindDispose;
        private IBindable<bool> Bindable;
        private string bindHint;

        #endregion


        /// <summary>
        /// <see cref="CheckProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="CheckPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public CheckProperty(CheckPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            isChecked = metadata.DefaultIsChecked;
        }


        private List<IObserver<bool>> Collection => list ??= new();
        /// <summary>
        /// チェックされている場合 <see langword="true"/>、そうでない場合は <see langword="false"/> となります
        /// </summary>
        [DataMember]
        public bool IsChecked
        {
            get => isChecked;
            set => SetValue(value, ref isChecked, checkedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.isChecked);
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
        public bool Value => IsChecked;


        #region Methods

        #region IBindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(bool value)
        {
            IsChecked = value;
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
        public IDisposable Subscribe(IObserver<bool> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), o =>
            {
                o.observer.OnCompleted();
                o.Item2.Collection.Remove(o.observer);
            });
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            BindDispose?.Dispose();
            Bindable = bindable;

            if (bindable is not null)
            {
                IsChecked = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        public override void Loaded()
        {
            if (IsLoaded) return;

            if (bindHint is not null)
            {
                if (this.GetBindable(bindHint, out var b))
                {
                    Bind(b);
                }
            }
            bindHint = null;

            base.Loaded();
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsChecked:{IsChecked} Name:{PropertyMetadata?.Name})";

        #endregion


        /// <summary>
        /// チェックされているかを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeCheckedCommand : IRecordCommand
        {
            private readonly CheckProperty CheckSetting;
            private readonly bool value;

            /// <summary>
            /// <see cref="ChangeCheckedCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="CheckProperty"/></param>
            /// <param name="value">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeCheckedCommand(CheckProperty property, bool value)
            {
                CheckSetting = property ?? throw new ArgumentNullException(nameof(property));
                this.value = value;
            }

            public string Name => CommandName.ChangeIsChecked;

            /// <inheritdoc/>
            public void Do() => CheckSetting.IsChecked = value;
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo() => CheckSetting.IsChecked = !value;
        }
    }

    /// <summary>
    /// <see cref="BEditor.Core.Data.Property.CheckProperty"/> のメタデータを表します
    /// </summary>
    public record CheckPropertyMetadata(string Name, bool DefaultIsChecked = false) : PropertyElementMetadata(Name);
}
