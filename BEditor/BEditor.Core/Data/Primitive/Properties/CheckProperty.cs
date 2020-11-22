using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// チェックボックスのプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class CheckProperty : PropertyElement, IEasingProperty, IBindable<bool>
    {
        #region フィールド

        private static readonly PropertyChangedEventArgs checkedArgs = new(nameof(IsChecked));
        private bool isChecked;
        private List<IObserver<bool>> list;

        private IDisposable BindDispose;

        #endregion
 

        /// <summary>
        /// <see cref="CheckProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="CheckPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public CheckProperty(CheckPropertyMetadata metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            PropertyMetadata = metadata;
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
            set => SetValue(value, ref isChecked, checkedArgs, CheckProperty_PropertyChanged);
        }
        /// <inheritdoc/>
        [DataMember]
        public string BindHint { get; private set; }
        /// <inheritdoc/>
        public bool Value => IsChecked;


        private void CheckProperty_PropertyChanged()
        {
            foreach (var observer in Collection)
            {
                try
                {
                    observer.OnNext(isChecked);
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }

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
        public IDisposable Subscribe(IObserver<bool> observer)
        {
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            BindDispose?.Dispose();

            if (bindable is not null)
            {
                BindHint = bindable.GetString();
                IsChecked = bindable.Value;

                // bindableが変更時にthisが変更
                BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            
            if(BindHint is not null)
            {
                if (this.GetBindable(BindHint, out var b))
                {
                    Bind(b);
                }
            }
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsChecked:{IsChecked} Name:{PropertyMetadata?.Name})";

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

            /// <inheritdoc/>
            public void Do() => CheckSetting.IsChecked = value;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => CheckSetting.IsChecked = !value;
        }
    }

    /// <summary>
    /// <see cref="CheckProperty"/> のメタデータを表します
    /// </summary>
    public record CheckPropertyMetadata(string Name, bool DefaultIsChecked = false) : PropertyElementMetadata(Name);
}
